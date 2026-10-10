using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
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

    internal static async Task RunCreationBudgetRibbonAsync(string contentRoot)
    {
        var owners = new ControlledLinkedOwner();
        await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners,
            creationFinalization: true, creationAttributes: true, creationSkills: true,
            productionCreationOverview: true);
        var saved = PrepareActualFinalizationReadyContext(runtime);
        await HydrateFinalizationOwnerAsync(runtime, owners, saved);
        AssertCreationReadinessCopy(runtime.Coordinator);
        var attributes = runtime.Coordinator.LoadCreationAttributes();
        var skills = runtime.Coordinator.LoadCreationSkills();
        var resourcesPresenter = new CharacterCreationResourcesInteractionPresenter(
            runtime.Services.GetRequiredService<ICharacterCreationResourcesService>(),
            runtime.Services.GetRequiredService<IOwnerBoundCharacterCreationResourcesService>());
        var resources = resourcesPresenter.Load(runtime.Coordinator.State);
        Require(resources.State is { } resourceState
            && CreationResourcesPhoneAuthority.IsReady(resourceState, runtime.Coordinator.State),
            "SETUP: Resources must be issued from the actual current owner.");
        Require(attributes.Value is { } attributeState
            && CreationAttributesPhoneAuthority.IsReady(attributeState, runtime.Coordinator.State)
            && skills.Value is { } skillState
            && CreationSkillsPhoneAuthority.IsReady(skillState, runtime.Coordinator.State),
            "SETUP: saved runner lacks current typed Attributes/Skills authority.");
        CharacterCreationBudgetState[] attributeBudgets =
            [attributes.Value!.NormalPointBudget, attributes.Value.SpecialPointBudget,
                attributes.Value.CreationKarmaBudget];
        CharacterCreationBudgetState[] skillBudgets =
            [skills.Value!.ActiveSkillPointBudget, skills.Value.SkillGroupPointBudget,
                skills.Value.KnowledgeSkillPointBudget];
        // The wizard's budget IDs differ from the typed allocation budget IDs.
        // Keep the real overview rows as input; only mark their fallback pending.
        string[] ids = [CharacterCreationBudgetIds.NormalAttributes, CharacterCreationBudgetIds.SpecialAttributes,
            CharacterCreationBudgetIds.Karma, CharacterCreationBudgetIds.ActiveSkills,
            CharacterCreationBudgetIds.SkillGroups, CharacterCreationBudgetIds.KnowledgeSkills];
        var unavailable = ids.Select(id => runtime.Coordinator.State.CreationWizard!.Budgets
            .Single(budget => budget.BudgetId == id) with
            { IsExact = false, Blockers = ["budget-authority-pending"] }).ToArray();
        var snapshot = runtime.Coordinator.State.CreationWizard! with { Budgets = unavailable };
        var render = typeof(BuildPage).GetMethod("AddBudgetRibbon", BindingFlags.Instance | BindingFlags.NonPublic)!;
        foreach (bool attributesReady in new[] { false, true })
        foreach (bool skillsReady in new[] { false, true })
        {
            var page = new BuildPage(runtime.Coordinator);
            var readiness = new CreationDashboardRenderReadiness(
                () => attributesReady, () => skillsReady, () => false,
                () => false, () => false, () => false);
            render.Invoke(page, [snapshot, attributes, skills, readiness, null, null, null]);
            var body = (VerticalStackLayout)((ScrollView)page.Content!).Content!;
            var cards = body.Children.OfType<FlexLayout>().Single().Children.OfType<Border>().ToArray();
            Require(cards.Length == 6, "A saved budget disappeared from the ribbon.");
            var sharedHints = body.Children.OfType<Label>()
                .Where(label => label.AutomationId == "creation-budget-status").ToArray();
            Require(sharedHints.Length == (attributesReady && skillsReady ? 0 : 1),
                "Explain incomplete budgets once, only when effective typed authority remains inexact.");
            Require(sharedHints.Length == 0 || sharedHints[0].Text ==
                "Some budgets need more choices. Tap a budget to continue.",
                "Incomplete budgets lost their shared explanation.");
            for (int index = 0; index < cards.Length; index++)
            {
                bool ready = index < attributeBudgets.Length ? attributesReady : skillsReady;
                var expected = index < attributeBudgets.Length
                    ? attributeBudgets[index] : skillBudgets[index - attributeBudgets.Length];
                var labels = ((Grid)cards[index].Content!).Children.OfType<VerticalStackLayout>().Single().Children.OfType<Label>()
                    .Select(label => label.Text).ToArray();
                Require(labels[0].EndsWith(ready
                        ? expected.Remaining.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " left"
                        : "Not exact", StringComparison.Ordinal),
                    $"Budget {expected.BudgetId} lost its own readiness: attributes={attributesReady}, skills={skillsReady}.");
                string expectedDetail = ready
                    ? expected.Used.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " / "
                        + expected.Total.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
                        + " " + (string.IsNullOrWhiteSpace(expected.Unit) ? "points" : expected.Unit)
                        + "\nCheck what is missing"
                    : "Check what is missing";
                Require(labels[1] == expectedDetail,
                    "Budget cards must retain exact numbers without repeated paragraphs or duplicate chevrons.");
                Require(((Grid)cards[index].Content!).Children.OfType<Button>().Single() is
                    { IsEnabled: true, AutomationId: not null }, "Budget is still a non-interactive label.");
            }
        }
        Require(CreationDashboardProjectionBinding.TryCreate(runtime.Coordinator.State,
            runtime.Coordinator.State.CreationWizard!, out var binding), "SETUP: no current dashboard binding.");
        // Inapplicable Mundane budgets add six cards but no choices. Keep every
        // uncertain/blocked/nonzero value and leave the actual stage route alone.
        Require(runtime.Coordinator.State.CreationMagicResonanceEditor?.Talent.Kind
            == CharacterCreationMagicResonanceKinds.Mundane, "SETUP: ribbon fixture must be Mundane.");
        string[] magicBudgetIds = [CharacterCreationBudgetIds.SpellsFormsPrograms,
            CharacterCreationMagicResonancePresentationBudgetIds.Tradition,
            CharacterCreationMagicResonancePresentationBudgetIds.Stream,
            CharacterCreationMagicResonancePresentationBudgetIds.AdeptPowerPoints,
            CharacterCreationMagicResonancePresentationBudgetIds.Spells,
            CharacterCreationMagicResonancePresentationBudgetIds.ComplexForms];
        var emptyMagic = magicBudgetIds.Select(id => snapshot.Budgets[0] with
            { BudgetId = id, IsExact = true, Total = 0, Used = 0, Remaining = 0, Blockers = [] }).ToArray();
        foreach (var budget in emptyMagic)
        {
            Require(!BuildPageUiProjection.ShowCreationBudget(budget, true),
                "An exact empty Mundane magic budget should not take a card.");
            Require(BuildPageUiProjection.ShowCreationBudget(budget, false),
                "A missing/stale or non-Mundane authority must not hide budgets.");
            foreach (var visible in new[]
            {
                budget with { IsExact = false },
                budget with { Blockers = ["budget-authority-pending"] },
                budget with { Total = 1 },
                budget with { Used = 1, Remaining = -1 },
                budget with { Remaining = 1 },
                budget with { BudgetId = CharacterCreationBudgetIds.SpecialAttributes },
                budget with { BudgetId = CharacterCreationBudgetIds.Resources },
                budget with { BudgetId = "unknown-budget" }
            })
                Require(BuildPageUiProjection.ShowCreationBudget(visible, true),
                    "Compact cards concealed points, a blocker or an unrelated budget.");
        }
        foreach (bool magicReady in new[] { false, true })
        {
            var page = new BuildPage(runtime.Coordinator);
            var readiness = new CreationDashboardRenderReadiness(
                () => false, () => false, () => false, () => magicReady, () => false, () => false);
            render.Invoke(page, [snapshot with { Budgets = [.. snapshot.Budgets, .. emptyMagic] },
                attributes, skills, readiness, null, null, null]);
            var body = (VerticalStackLayout)((ScrollView)page.Content!).Content!;
            var cards = body.Children.OfType<FlexLayout>().Single().Children.OfType<Border>().ToArray();
            Require(cards.Length == (magicReady ? snapshot.Budgets.Count : snapshot.Budgets.Count + 6),
                "The rendered ribbon did not follow current Mundane authority.");
            Require(body.Children.OfType<Label>().Any(label => label.AutomationId == "creation-budget-status"),
                "Unrelated inexact budgets lost their explanation.");
        }
        var projection = CreationDashboardAuthorityProjection.Loading(binding!) with { Resources = resources };
        var resourceFallback = runtime.Coordinator.State.CreationWizard!.Budgets.Single(
            row => row.BudgetId == CharacterCreationBudgetIds.Resources);
        Require(!resourceFallback.IsExact, "SETUP: expected the conservative Resources placeholder.");
        foreach (bool ready in new[] { false, true })
        {
            var page = new BuildPage(runtime.Coordinator);
            var readiness = new CreationDashboardRenderReadiness(
                () => false, () => false, () => false, () => false, () => false, () => ready);
            render.Invoke(page, [snapshot with { Budgets = [resourceFallback] },
                attributes, skills, readiness, null, projection, null]);
            var body = (VerticalStackLayout)((ScrollView)page.Content!).Content!;
            var card = body.Children.OfType<FlexLayout>().Single().Children.OfType<Border>().Single();
            Require(body.Children.OfType<Label>().Count(label => label.AutomationId == "creation-budget-status")
                == (ready ? 0 : 1), "The shared hint must follow typed Resources authority too.");
            var label = ((Grid)card.Content!).Children.OfType<VerticalStackLayout>().Single().Children.OfType<Label>().First();
            Require(label.Text.EndsWith(ready
                ? resources.State!.Budget.RemainingNuyen.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " left"
                : "Not exact", StringComparison.Ordinal),
                "Resources must leave Not exact only when its own typed projection is ready.");
        }
        using (var ui = new IssuedPageUiContext())
        await ui.RunAsync(async () =>
        {
            var page = new BuildPage(runtime.Coordinator, resourcesPresenter, runtime.Presenter);
            var nav = new NavigationPage(page);
            _ = new Window(nav);
            var readiness = new CreationDashboardRenderReadiness(
                () => true, () => true, () => false, () => false, () => false, () => true);
            var stages = typeof(BuildPage).GetMethod("AddWizardStages", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var routes = (IReadOnlyDictionary<string, CreationBudgetRoute>)stages.Invoke(page,
                [snapshot, null, null, attributes, skills, null, resources, readiness])!;
            // Feed the real shared stage routes to the budget cards. Generic
            // budget readiness is not a grant to a different destination.
            var pending = new CreationDashboardRenderReadiness(
                () => false, () => false, () => false, () => false, () => false, () => false);
            render.Invoke(page, [snapshot, attributes, skills, pending, routes, null, 0]);
            var body = (VerticalStackLayout)((ScrollView)page.Content!).Content!;
            var buttons = body.Children.OfType<FlexLayout>().Single().Children.OfType<Border>()
                .Select(card => ((Grid)card.Content!).Children.OfType<Button>().Single()).ToArray();
            foreach (int i in new[] { 0, 1, 3, 4, 5 })
            {
                await ui.BeginAsyncVoid(() => ((IButtonController)buttons[i]).SendClicked());
                Require(i < 2 ? nav.Navigation.NavigationStack.Last() is CreationAttributesPage
                    : nav.Navigation.NavigationStack.Last() is CreationSkillsPage,
                    $"Budget {ids[i]} did not open its actual typed editor.");
                await nav.PopAsync(false);
            }
            // Resources remains generically inexact before the typed overlay.
            // Its budget link must still open the real Resources page.
            render.Invoke(page, [snapshot with { Budgets = [resourceFallback] },
                attributes, skills, pending, routes, projection, null]);
            var resourceButton = body.Children.OfType<FlexLayout>().Last().Children.OfType<Border>()
                .Select(card => ((Grid)card.Content!).Children.OfType<Button>().Single()).Single();
            await ui.BeginAsyncVoid(() => ((IButtonController)resourceButton).SendClicked());
            Require(nav.Navigation.NavigationStack.Last() is CreationResourcesPage,
                "Inexact Resources did not open the admitted Resources editor.");
            await nav.PopAsync(false);
            var karma = snapshot.Budgets.Single(row => row.BudgetId == CharacterCreationBudgetIds.Karma)
                with { Blockers = [CharacterCreationQualitiesBlockers.AttributesDraftRequired] };
            render.Invoke(page, [snapshot with { Budgets = [karma] },
                attributes, skills, pending, routes, projection, null]);
            var dependencyButton = body.Children.OfType<FlexLayout>().Last().Children.OfType<Border>()
                .Select(card => ((Grid)card.Content!).Children.OfType<Button>().Single()).Single();
            await ui.BeginAsyncVoid(() => ((IButtonController)dependencyButton).SendClicked());
            Require(nav.Navigation.NavigationStack.Last() is CreationAttributesPage,
                "A blocked Qualities/Karma budget did not lead to its missing Attributes prerequisite.");
            await nav.PopAsync(false);
            // The card is retained across an A→B→A switch. Matching names must
            // not revive the old click or navigate to another runner's editor.
            owners.Set(ContactsOwnerB);
            owners.Set(ContactsOwnerA);
            await ui.BeginAsyncVoid(() => ((IButtonController)buttons[0]).SendClicked());
            Require(nav.Navigation.NavigationStack.Count == 1,
                "A retained budget card reopened an editor after owner transition.");
            ui.AssertHealthy();
        });
        var cold = new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!;
        Require(FinalizationDocumentDigest(cold) == FinalizationDocumentDigest(saved),
            "Rendering mixed budget families changed the saved runner.");
        Console.WriteLine("PASS budget ribbon: compact cards, one effective-readiness hint, typed family readiness, actual editors, stale owner rejection, saved bytes unchanged");
        await VerifySavedQualitiesKarmaBudgetAsync(contentRoot);
    }

    private static async Task VerifySavedQualitiesKarmaBudgetAsync(string contentRoot)
    {
        foreach (string method in new[] { CharacterCreationBuildMethods.Priority, CharacterCreationBuildMethods.SumToTen })
        {
            var owners = new ControlledLinkedOwner();
            await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners,
                creationFinalization: true, creationAttributes: true, creationSkills: true,
                productionCreationOverview: true);
            var before = PrepareActualFinalizationReadyContext(runtime, stopBeforeQualities: true, buildMethod: method);
            await HydrateFinalizationOwnerAsync(runtime, owners, before);
            var service = runtime.Services.GetRequiredService<ICharacterCreationQualitiesService>();
            var state = runtime.Coordinator.State.CreationQualities!;
            Require(CreationQualitiesPhoneAuthority.IsReady(state, runtime.Coordinator.State),
                "SETUP: Qualities must be current before choosing a real quality.");
            var draft = new CreationQualitiesPhoneDraft();
            draft.Bind(state, runtime.Coordinator.State);
            var editor = CreationQualitiesPhoneAuthority.ProjectEditor(state, runtime.Coordinator.State);
            var option = draft.AvailableOptions(state, runtime.Coordinator.State, editor, default)
                .First(item => item.Name == "Acrobatic Defender" && item.KarmaCost == 4);
            var render = typeof(BuildPage).GetMethod("AddBudgetRibbon", BindingFlags.Instance | BindingFlags.NonPublic)!;

            // Save and then remove the real selection through Core, not a forged
            // snapshot. Cold-store reload must agree with the dashboard each time.
            foreach (string[] selection in new[] { new[] { option.OptionId }, Array.Empty<string>() })
            {
                state = service.Load(new(runtime.Id)).Value!;
                var preview = service.Preview(new(state.Binding, selection)).Value!;
                Require(preview.CanConfirm, "SETUP: real quality selection must be confirmable.");
                var result = service.Confirm(new(preview.Binding, selection, preview.PreviewDigest,
                    "budget-karma-" + selection.Length, Guid.NewGuid(), true));
                Require(result.Outcome == CharacterCreationFoundationOutcomes.Success,
                    "SETUP: Core did not save the quality selection.");
                var saved = new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!;
                await HydrateFinalizationOwnerAsync(runtime, owners, saved);
                state = runtime.Coordinator.State.CreationQualities!;
                Require(CreationQualitiesPhoneAuthority.IsReady(state, runtime.Coordinator.State)
                    && state.Preview.KarmaRemaining == (selection.Length == 1 ? 21 : 25),
                    "SETUP: saved Qualities must issue the cumulative Karma remainder.");
                var attributes = runtime.Coordinator.LoadCreationAttributes();
                Require(attributes.Value!.CreationKarmaBudget.Remaining == 25,
                    "SETUP: Attributes must still describe only the earlier allocation step.");
                var snapshot = runtime.Coordinator.State.CreationWizard!;
                var karma = snapshot.Budgets.Single(row => row.BudgetId == CharacterCreationBudgetIds.Karma);
                Require(karma.IsExact && karma.Remaining == state.Preview.KarmaRemaining,
                    "SETUP: Presentation must project Core's cumulative Qualities budget.");
                foreach (bool attributesReady in new[] { false, true })
                foreach (bool exact in new[] { true, false })
                {
                    var page = new BuildPage(runtime.Coordinator);
                    var readiness = new CreationDashboardRenderReadiness(
                        () => attributesReady, () => false, () => true,
                        () => false, () => false, () => false);
                    render.Invoke(page, [snapshot with { Budgets = [karma with { IsExact = exact }] },
                        attributes, null, readiness, null, null, null]);
                    var body = (VerticalStackLayout)((ScrollView)page.Content!).Content!;
                    var card = body.Children.OfType<FlexLayout>().Single().Children.OfType<Border>().Single();
                    var labels = ((Grid)card.Content!).Children.OfType<VerticalStackLayout>().Single()
                        .Children.OfType<Label>().Select(label => label.Text).ToArray();
                    string number = karma.Remaining.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                    Require(labels[0].EndsWith(exact ? number + " left" : "Not exact", StringComparison.Ordinal),
                        $"{method}: saved Qualities Karma {number} was replaced by the earlier Attributes budget; exact={exact}, attributes={attributesReady}.");
                    Require(!exact || labels[1].StartsWith(
                        $"{karma.Used} / {karma.Total} karma", StringComparison.Ordinal),
                        "Cumulative Karma used/total must match the same projection as its remainder.");
                    Require(body.Children.OfType<Label>().Count(label => label.AutomationId == "creation-budget-status")
                        == (exact ? 0 : 1), "An inexact cumulative budget must not become exact from earlier Attributes.");
                }
                RequireSameRewardDocument(saved, new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!);
            }
        }
        Console.WriteLine("PASS Priority/Sum-to-Ten cumulative Karma: real +4 quality save, cold reopen, removal, no earlier-budget fallback, unchanged rendered bytes");
    }

    internal static async Task RunCreationContinueRoutesAsync(string contentRoot)
    {
        foreach (string buildMethod in new[] { CharacterCreationBuildMethods.Priority, CharacterCreationBuildMethods.SumToTen })
        {
            var owners = new ControlledLinkedOwner();
            await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners,
                creationPrerequisite: true, productionCreationOverview: true);
            var saved = PreparePrerequisiteOwnerFixture(runtime, buildMethod);
            await HydrateFinalizationOwnerAsync(runtime, owners, saved);
            using var ui = new IssuedPageUiContext();
            await ui.RunAsync(async () =>
            {
                var prerequisite = await runtime.Coordinator.LoadCreationPrerequisiteAsync();
                Require(prerequisite.Value is { } authority
                    && CreationPrerequisitePhoneAuthority.IsReady(authority, runtime.Coordinator.State)
                    && runtime.Coordinator.IsCreationPrerequisiteStateCurrent(authority),
                    "SETUP: fresh runner must have exact, openable method authority.");
                var snapshot = runtime.Coordinator.State.CreationWizard!;
                Require(snapshot.ActiveStepId == CharacterCreationWizardStepIds.Method,
                    "SETUP: fresh runner must actually be at Creation method.");
                var readiness = new CreationDashboardRenderReadiness(
                    () => false, () => false, () => false, () => false, () => false, () => false);
                var methodRender = typeof(BuildPage).GetMethod("AddCreationMethodRoute", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var nextRender = typeof(BuildPage).GetMethod("AddLegalNextSteps", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var stages = typeof(BuildPage).GetMethod("AddWizardStages", BindingFlags.Instance | BindingFlags.NonPublic)!;
                foreach (bool available in new[] { false, true })
                {
                    var page = new BuildPage(runtime.Coordinator);
                    var nav = new NavigationPage(page);
                    _ = new Window(nav);
                    var typed = available ? prerequisite : null;
                    var methodRoute = (CreationBudgetRoute)methodRender.Invoke(page, [snapshot, null, typed])!;
                    var routes = (IReadOnlyDictionary<string, CreationBudgetRoute>)stages.Invoke(page,
                        [snapshot, null, typed, null, null, null, null, readiness])!;
                    nextRender.Invoke(page, [snapshot, readiness, routes, methodRoute]);
                    var body = (VerticalStackLayout)((ScrollView)page.Content!).Content!;
                    var cards = body.Children.OfType<Border>().Where(border => border.Content is Grid)
                        .Select(border => (Grid)border.Content!).ToArray();
                    var methodButton = cards.SelectMany(grid => grid.Children.OfType<Button>())
                        .Single(button => button.AutomationId == "creation-next-method");
                    Require(methodButton.IsEnabled == available && methodRoute.CanOpen == available,
                        "Continue must agree with the canonical method route and never borrow generic legality.");
                    foreach (var grid in cards)
                    {
                        var button = grid.Children.OfType<Button>().Single();
                        if (button.AutomationId?.StartsWith("creation-next-", StringComparison.Ordinal) != true) continue;
                        string step = button.AutomationId["creation-next-".Length..];
                        var route = step == CharacterCreationWizardStepIds.Method ? methodRoute : routes[step];
                        Require(button.IsEnabled == route.CanOpen && grid.Children.OfType<VerticalStackLayout>()
                            .Single().Children.OfType<Label>().Any(label => label.Text == route.Detail),
                            "Continue lost the shared readiness/scope explanation.");
                    }
                    if (available)
                    {
                        await ui.BeginAsyncVoid(() => ((IButtonController)methodButton).SendClicked());
                        Require(nav.CurrentPage is CreationPrerequisitePage,
                            "Continue did not open the real typed Creation method editor.");
                        await nav.PopAsync(false);
                        owners.Set(ContactsOwnerB);
                        owners.Set(ContactsOwnerA);
                        await ui.BeginAsyncVoid(() => ((IButtonController)methodButton).SendClicked());
                    }
                    else ((IButtonController)methodButton).SendClicked();
                    Require(nav.Navigation.NavigationStack.Count == 1,
                        "A blocked or stale-owner Continue link still navigated.");
                }
                ui.AssertHealthy();
            });
            var cold = new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!;
            Require(FinalizationDocumentDigest(cold) == FinalizationDocumentDigest(saved),
                "Continue navigation changed the saved runner.");
            Console.WriteLine($"PASS Continue {buildMethod}: actual method editor, blocked/stale-owner rejection, shared detail, saved bytes unchanged");
        }
    }

    internal static async Task RunCreationReviewRoutesAsync(string contentRoot)
    {
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            var app = new ReviewRouteApplication();
            Application.Current = app;
            // Use MAUI's window-created entry: OpenWindow alone queues a native
            // request and does not register a window without a platform handler.
            var window = (Window)((Microsoft.Maui.IApplication)app).CreateWindow(null!);
            foreach (string method in new[] { CharacterCreationBuildMethods.Priority, CharacterCreationBuildMethods.SumToTen })
            {
                var owners = new ControlledLinkedOwner();
                await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners,
                    creationFinalization: true, productionCreationOverview: true);
                var saved = PrepareActualFinalizationReadyContext(runtime, buildMethod: method);
                await HydrateFinalizationOwnerAsync(runtime, owners, saved);
                var authority = runtime.Coordinator.LoadCreationFinalization();
                Require(authority.Value is { CanReview: true }, "SETUP: saved runner must be reviewable.");
                var snapshot = runtime.Coordinator.State.CreationWizard! with
                {
                    // Exercise Continue's Review entry independently of which earlier
                    // optional step the generic overview currently highlights.
                    ActiveStepId = CharacterCreationWizardStepIds.Review
                };
                foreach (string state in new[] { "loading", "blocked", "ready", "old-render", "owner-aba" })
                foreach (string entry in new[] { "creation-stage-review", "creation-next-review", "creation-finalization-open-review" })
                {
                    await HydrateFinalizationOwnerAsync(runtime, owners, saved);
                    authority = runtime.Coordinator.LoadCreationFinalization();
                    var page = new BuildPage(runtime.Coordinator);
                    var shell = new Shell();
                    shell.Items.Add(new ShellContent { Content = page });
                    var scroll = (ScrollView)page.Content!;
                    scroll.ScrollToRequested += (_, _) => scroll.SendScrollFinished();
                    void Invoke(string methodName, params object?[] args) => typeof(BuildPage)
                        .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, args);
                    window.Page = shell;
                    await ui.DrainDispatchedAsyncVoidAsync();
                    Require(ReferenceEquals(Shell.Current?.CurrentPage, page),
                        $"SETUP: review dashboard is not the active Shell page (windows={app.Windows.Count}, currentApp={ReferenceEquals(Application.Current, app)}, localPage={ReferenceEquals(shell.CurrentPage, page)}).");
                    // Settle native appearance work, then isolate the render-local
                    // input under test from later background projection callbacks.
                    Invoke("OnDisappearing");
                    Invoke("BeginRunnerLoad");
                    typeof(BuildPage).GetField("_creationFinalizationAuthority", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .SetValue(page, state == "loading" ? null : state == "blocked"
                            ? authority with { Value = authority.Value! with { CanReview = false } } : authority);
                    var readiness = new CreationDashboardRenderReadiness(
                        () => false, () => false, () => false, () => false, () => false, () => false);
                    var routes = (IReadOnlyDictionary<string, CreationBudgetRoute>)typeof(BuildPage)
                        .GetMethod("AddWizardStages", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(page, [snapshot, null, null, null, null, null, null, readiness])!;
                    Invoke("AddLegalNextSteps", snapshot, readiness, routes,
                        new CreationBudgetRoute("Method", "", false, () => Task.CompletedTask, []));
                    Invoke("AddFinalizationReviewAction");
                    bool available = state is not ("loading" or "blocked");
                    var route = routes[CharacterCreationWizardStepIds.Review];
                    Require(route.CanOpen == available, $"Review route {method}/{state} disagrees with exact finalization readiness.");
                    if (!available)
                        Require(route.Detail != "Available", "A blocked Review still claims to be available.");
                    var button = IssuedElements(page).OfType<Button>().SingleOrDefault(row => row.AutomationId == entry);
                    if (entry == "creation-finalization-open-review" && !available)
                        Require(button is null, "Blocked finalization still exposes its primary action.");
                    else
                    {
                        Require(button is not null && button.IsEnabled == available,
                            $"Review control {entry}/{method}/{state} disagrees with the shared route.");
                        if (state == "old-render") Invoke("BeginRunnerLoad");
                        if (state == "owner-aba")
                        {
                            owners.Set(ContactsOwnerB);
                            owners.Set(OwnerScope.LocalSingleUser);
                        }
                        using var alerts = new IssuedPageAlerts(page, window);
                        if (available)
                            await ui.BeginAsyncVoid(() => ((IButtonController)button!).SendClicked()).WaitAsync(TimeSpan.FromSeconds(30));
                        else
                            ((IButtonController)button!).SendClicked();
                        Require(alerts.Titles.Count == 0, "Review navigation raised an unexpected alert: " + string.Join("; ", alerts.Messages));
                        Require((page.Navigation.NavigationStack.LastOrDefault() is CreationStartingCashPage) == (state == "ready"),
                            $"Review control {entry}/{method}/{state} reached the wrong destination.");
                    }
                    Invoke("OnDisappearing");
                    window.Page = new ContentPage();
                    await ui.DrainDispatchedAsyncVoidAsync();
                    Require(FinalizationDocumentDigest(new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!)
                        == FinalizationDocumentDigest(saved), "Opening or rejecting final review changed the saved runner.");
                    Console.WriteLine($"PASS Review {method}/{entry}/{state}: exact admission and unchanged saved runner");
                }
            }
            app.CloseWindow(window);
        });
    }

    private sealed class ReviewRouteApplication : Application
    {
        protected override Window CreateWindow(Microsoft.Maui.IActivationState? activationState) => new();
    }

    private static void AssertCreationReadinessCopy(RunnerSessionCoordinator coordinator)
    {
        var snapshot = coordinator.State.CreationWizard! with
        {
            CompletionBlockers = ["creation-finalization-attributes-draft-required",
                "creation-skills-attributes-draft-required", "fixture-unknown-blocker"]
        };
        string snapshotBefore = JsonSerializer.Serialize(snapshot);
        var legacyPage = new BuildPage(coordinator);
        typeof(BuildPage).GetMethod("AddCompletionBlockers", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(legacyPage, [snapshot]);
        var legacyBody = (VerticalStackLayout)((ScrollView)legacyPage.Content!).Content!;
        var legacyCard = (VerticalStackLayout)legacyBody.Children.OfType<Border>().Single().Content!;
        var legacyLabels = legacyCard.Children.OfType<Label>().ToArray();
        Require(legacyLabels.Count(label => label.Text == CreationFlowStrings.FinalizationBlocker(snapshot.CompletionBlockers[0])) == 1
            && legacyLabels.Any(label => label.Text == CreationFlowStrings.DashboardBlocker("fixture-unknown-blocker"))
            && !legacyLabels.Any(label => label.Text == "fixture-unknown-blocker"),
            "Completion must show deduplicated guidance and keep unknown codes in explicit diagnostics.");
        var legacyDetails = legacyCard.Children.OfType<VerticalStackLayout>().Single();
        var legacyToggle = legacyCard.Children.OfType<Button>().Single();
        Require(!legacyDetails.IsVisible && snapshot.CompletionBlockers.All(code =>
            legacyDetails.Children.OfType<Label>().Any(label => label.Text == code)),
            "Legacy completion details must be collapsed and preserve every exact code.");
        ((IButtonController)legacyToggle).SendClicked();
        Require(legacyDetails.IsVisible, "Legacy blocker diagnostics did not expand.");
        ((IButtonController)legacyToggle).SendClicked();
        legacyBody.Clear();
        ((IButtonController)legacyToggle).SendClicked();
        Require(!legacyDetails.IsVisible && JsonSerializer.Serialize(snapshot) == snapshotBefore,
            "A detached blocker card accepted a click or changed the Core projection.");
        var actual = CreationPriorityLegalPathProjection.From(coordinator.LoadCreationFinalization());
        Require(actual.CanOpenReview, "SETUP: the saved fixture must be ready for final review.");
        var render = typeof(BuildPage).GetMethod("AddCreationFinalizationStatus",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        foreach (var projection in new[]
        {
            actual,
            actual with
            {
                CanOpenReview = false,
                Blockers = ["fixture-whole-build-blocker", "creation-finalization-attributes-draft-required"],
                Steps = [actual.Steps[0] with
                {
                    IsComplete = false,
                    Blockers = ["fixture-first-blocker", "fixture-second-blocker",
                        "creation-finalization-attributes-draft-required", "creation-skills-attributes-draft-required"]
                }]
            }
        })
        {
            string before = JsonSerializer.Serialize(projection);
            var page = new BuildPage(coordinator);
            render.Invoke(page, [projection]);
            var body = (VerticalStackLayout)((ScrollView)page.Content!).Content!;
            var card = (VerticalStackLayout)body.Children.OfType<Border>().Single().Content!;
            var rows = card.Children.OfType<Label>().ToArray();
            Require(rows.Where(row => row.AutomationId?.StartsWith("creation-finalization-step-", StringComparison.Ordinal) == true)
                .All(row => !row.Text.Contains("source anchor", StringComparison.OrdinalIgnoreCase)),
                "Completed step rows still present diagnostic source counts as gameplay feedback.");
            Require(projection.Steps.SelectMany(step => step.Blockers).Concat(projection.Blockers)
                .All(blocker => rows.Any(row => row.IsVisible && row.Text == CreationFlowStrings.DashboardBlocker(blocker))),
                "Readability must not hide any step or whole-build blocker.");
            Require(rows.Select(row => row.Text).Distinct(StringComparer.Ordinal).Count() == rows.Length,
                "Repeated prerequisites still flood the readiness card with duplicate instructions.");
            var diagnostics = card.Children.OfType<VerticalStackLayout>().Single(
                child => child.AutomationId == "creation-finalization-readiness-details");
            Require(!diagnostics.IsVisible, "Technical readiness metadata must start collapsed.");
            var toggle = card.Children.OfType<Button>().Single();
            ((IButtonController)toggle).SendClicked();
            Require(diagnostics.IsVisible, "Readiness details did not expand.");
            Require(diagnostics.Children.OfType<Label>().Any(row => row.Text.Contains(projection.SnapshotDigest!, StringComparison.Ordinal)),
                "Technical details lost the exact snapshot digest.");
            Require(projection.Steps.SelectMany(step => step.Blockers).Concat(projection.Blockers)
                .All(blocker => diagnostics.Children.OfType<Label>().Any(row => row.Text == blocker)),
                "Readable warnings lost their exact diagnostic blocker codes.");
            foreach (var step in projection.Steps)
                Require(step.SourceAnchorIds.All(anchor => diagnostics.Children.OfType<Label>()
                    .Any(row => row.Text.Contains(anchor, StringComparison.Ordinal))),
                    "Technical details lost an exact source anchor.");
            ((IButtonController)toggle).SendClicked();
            Require(!diagnostics.IsVisible, "Readiness details did not collapse.");
            body.Clear();
            ((IButtonController)toggle).SendClicked();
            Require(!diagnostics.IsVisible, "A detached readiness card still accepted a click.");
            Require(JsonSerializer.Serialize(projection) == before,
                "Display formatting modified the Core-derived readiness projection.");
        }
        var culture = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("de");
            Require(CreationFlowStrings.FinalizationBlocker("creation-finalization-attributes-draft-required")
                == "Öffne Attribute, prüfe die Verteilung und speichere sie.",
                "The German readiness action was not localized.");
            Require(CreationFlowStrings.FinalizationBlocker("unknown-exact-blocker") == "unknown-exact-blocker",
                "An unknown blocker was hidden by a guessed translation.");
        }
        finally { System.Globalization.CultureInfo.CurrentUICulture = culture; }
    }

    internal static async Task RunCreationSpecializationPickerAsync(string contentRoot)
    {
        var owners = new ControlledLinkedOwner();
        await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners,
            creationFinalization: true, creationAttributes: true, creationSkills: true,
            productionCreationOverview: true);
        var before = PrepareActualFinalizationReadyContext(runtime, stopBeforeQualities: true);
        await HydrateFinalizationOwnerAsync(runtime, owners, before);
        var state = runtime.Coordinator.LoadCreationSkills().Value!;
        var source = CreationSkillsPhoneAuthority.AvailableActiveSkills(state)
            .First(item => item.Specializations.Count > 6);
        string token = new(source.SourceSkillId.ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-').ToArray());
        var readSaved = () => new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!;
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            var page = new CreationSkillsPage(runtime.Coordinator, state);
            _ = new Window(new NavigationPage(page));
            await ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing"));
            CreationSkillsPhoneDraft Draft() => (CreationSkillsPhoneDraft)typeof(CreationSkillsPage)
                .GetField("_draft", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(page)!;
            CharacterCreationSkillAllocation Chosen() => Draft().Skills.Single(item =>
                item.Kind == source.Kind && item.SourceSkillId == source.SourceSkillId);
            Border? Card() => MinimalVisible(page).OfType<Border>().SingleOrDefault(item =>
                item.AutomationId == "creation-skill-" + token);
            async Task FindCardAsync()
            {
                for (int count = 0; Card() is null && count < 20; count++)
                {
                    var next = MinimalVisible(page).OfType<Button>().Single(item =>
                        item.AutomationId == "creation-skills-active-catalog-next");
                    Require(next.IsEnabled, "Target skill was omitted from the paged catalog.");
                    await ui.BeginAsyncVoid(() => ((IButtonController)next).SendClicked());
                }
                Require(Card() is not null, "Target skill was not reachable.");
            }
            Picker Picker() => MinimalVisible(page).OfType<Picker>().Single(item =>
                item.AutomationId == "creation-skill-specialization-" + token);
            Button Choose() => MinimalVisible(page).OfType<Button>().Single(item =>
                item.AutomationId == "creation-skill-specialization-preview-" + token);
            async Task ClickAsync(Button button) => await ui.BeginAsyncVoid(() => ((IButtonController)button).SendClicked());
            await FindCardAsync();
            Require(!MinimalVisible(page).OfType<Picker>().Any(item =>
                item.AutomationId == "creation-skill-specialization-" + token),
                "An unrated skill offered a specialization.");
            var plus = ((VerticalStackLayout)Card()!.Content!).Children.OfType<HorizontalStackLayout>()
                .Single().Children.OfType<Button>().Single(item => item.Text == "+");
            await ClickAsync(plus);
            var picker = Picker();
            var choose = Choose();
            Require(picker.ItemsSource.Count == source.Specializations.Count + 1
                && picker.ItemsSource.Cast<string>().Skip(1).SequenceEqual(source.Specializations.Select(item => item.Name))
                && picker.SelectedIndex == 0 && !choose.IsEnabled && picker.TextColor == NativeTheme.Text,
                "Specialization list is truncated, reordered, unreadable or changes a no-op.");
            string untouched = JsonSerializer.Serialize(Draft().Skills);
            picker.SelectedIndex = source.Specializations.Count;
            Require(choose.IsEnabled && JsonSerializer.Serialize(Draft().Skills) == untouched
                && FinalizationDocumentDigest(readSaved()) == FinalizationDocumentDigest(before),
                "Picker selection itself spent points or persisted a draft.");
            await ClickAsync(choose);
            Require(Chosen().SpecializationOptionId == source.Specializations.Last().OptionId
                && Picker().SelectedIndex == source.Specializations.Count && !Choose().IsEnabled,
                "The option beyond six did not preview by exact ID or restore its displayed selection.");
            string adopted = JsonSerializer.Serialize(Draft().Skills);
            picker.SelectedIndex = 1;
            await ClickAsync(choose);
            Require(JsonSerializer.Serialize(Draft().Skills) == adopted,
                "A retained control from an earlier render changed the current proposal.");
            var noOp = Choose();
            Require(!noOp.IsEnabled, "An unchanged specialization must not offer a mutation.");
            ((IButtonController)noOp).SendClicked();
            Require(JsonSerializer.Serialize(Draft().Skills) == adopted,
                "Re-selecting the current specialization toggled it off.");
            Picker().SelectedIndex = 0;
            await ClickAsync(Choose());
            Require(Chosen().SpecializationOptionId is null && Picker().SelectedIndex == 0,
                "The explicit No specialization choice did not remove it.");
            Picker().SelectedIndex = source.Specializations.Count;
            await ClickAsync(Choose());
            Require(FinalizationDocumentDigest(readSaved()) == FinalizationDocumentDigest(before),
                "Previewing specialization changes mutated the saved workspace.");
            // Exercise the actual review page, including its ordinary (non-proof)
            // accessibility tree and explicit save, against the same Core preview.
            var preview = Draft().Preview!;
            string previewBytes = JsonSerializer.Serialize(preview);
            CreationSkillsPhoneConfirmResult? savedResult = null;
            var review = new CreationSkillsPreviewPage(runtime.Coordinator, preview,
                Draft().Skills, Draft().Groups, result => savedResult = result);
            IssuedPageLifecycle(page, "OnDisappearing");
            _ = new Window(new NavigationPage(review));
            await ui.BeginAsyncVoid(() => IssuedPageLifecycle(review, "OnAppearing"));
            VerticalStackLayout Details() => MinimalVisible(review).OfType<VerticalStackLayout>()
                .Single(item => item.AutomationId == "creation-skills-preview-details");
            void CheckDisclosure(bool afterSave)
            {
                MinimalRequireNoMachineValues(review);
                string visible = MinimalVisibleText(review);
                Require(visible.Contains(source.Name, StringComparison.Ordinal)
                    && visible.Contains(source.Specializations.Last().Name, StringComparison.Ordinal)
                    && visible.Contains(CreationAllocationStrings.Get("SkillsPreview.PointCost", ""), StringComparison.Ordinal)
                    && !visible.Contains(CreationAllocationStrings.Get("Common.ContentRevision", ""), StringComparison.Ordinal),
                    "Review hid a player choice/cost or exposed receipt internals.");
                var details = Details();
                var toggle = details.Children.OfType<Button>().Single();
                var content = details.Children.OfType<VerticalStackLayout>().Single();
                Require(!content.IsVisible, "Review diagnostics were expanded by default.");
                ((IButtonController)toggle).SendClicked();
                Require(content.IsVisible
                    && MinimalVisible(review).OfType<Label>().Any(item =>
                        item.AutomationId == "creation-skills-preview-digest" && item.Text == preview.PreviewDigest)
                    && MinimalVisible(review).OfType<Label>().Any(item =>
                        item.AutomationId == "creation-skills-preview-raw-character-xml-digest"
                        && item.Text == preview.Binding.RawCharacterXmlDigest),
                    "Explicit disclosure lost exact preview diagnostics or automation anchors.");
                if (afterSave)
                    Require(MinimalVisible(review).OfType<Label>().Any(item =>
                        item.AutomationId == "creation-skills-receipt-digest"
                        && item.Text == savedResult!.Receipt!.ReceiptDigest),
                        "Explicit disclosure lost the exact saved receipt digest.");
                ((IButtonController)toggle).SendClicked();
                Require(!content.IsVisible, "Review diagnostics would not collapse.");
                MinimalRequireNoMachineValues(review);
                MinimalRender(review);
                ((IButtonController)toggle).SendClicked();
                Require(!content.IsVisible, "A retained diagnostic toggle survived a new render.");
            }
            var originalCulture = System.Globalization.CultureInfo.CurrentUICulture;
            try
            {
                foreach (var (culture, heading, save) in new[]
                {
                    ("en", "Your skills", "Save skills"),
                    ("de", "Deine Fertigkeiten", "Fertigkeiten speichern"),
                    ("es", "Tus habilidades", "Guardar habilidades")
                })
                {
                    System.Globalization.CultureInfo.CurrentUICulture = new(culture);
                    MinimalRender(review);
                    Require(MinimalVisibleText(review).Contains(heading.ToUpperInvariant(), StringComparison.Ordinal)
                        && MinimalVisible(review).OfType<Button>().Any(item =>
                            item.AutomationId == "creation-skills-confirm" && item.Text == save && item.IsEnabled),
                        "Review did not render its readable localized selection/save copy.");
                    CheckDisclosure(afterSave: false);
                }
            }
            finally { System.Globalization.CultureInfo.CurrentUICulture = originalCulture; }
            Require(JsonSerializer.Serialize(preview) == previewBytes
                && FinalizationDocumentDigest(readSaved()) == FinalizationDocumentDigest(before),
                "Reading or disclosing a review changed the preview or saved runner.");
            MinimalRender(review);
            await ClickAsync(MinimalVisible(review).OfType<Button>().Single(item =>
                item.AutomationId == "creation-skills-confirm"));
            Require(savedResult?.Receipt is not null && savedResult.Blockers.Count == 0,
                "The last catalog specialization could not be saved: " + JsonSerializer.Serialize(savedResult));
            Require(MinimalVisibleText(review).Contains(
                    CreationAllocationStrings.Get("SkillsPreview.SavedHeading", "").ToUpperInvariant(), StringComparison.Ordinal)
                && MinimalVisibleText(review).Contains(
                    CreationAllocationStrings.Get("SkillsPreview.DurablePendingFinalization", ""), StringComparison.Ordinal)
                && !MinimalVisible(review).OfType<Button>().Any(item => item.AutomationId == "creation-skills-confirm"),
                "Save did not show a readable, distinct draft receipt or still offered confirmation.");
            CheckDisclosure(afterSave: true);
            var saved = readSaved();
            Require(saved.ContentRevision == before.ContentRevision + 1
                && saved.ContentRevision == saved.SavedRevision, "Specialization save was not once-only.");
            var departedDetails = Details();
            var departedToggle = departedDetails.Children.OfType<Button>().Single();
            var departedContent = departedDetails.Children.OfType<VerticalStackLayout>().Single();
            IssuedPageLifecycle(review, "OnDisappearing");
            ((IButtonController)departedToggle).SendClicked();
            Require(!departedContent.IsVisible, "Departed review exposed retained diagnostics.");
            await HydrateFinalizationOwnerAsync(runtime, owners, saved);
            state = runtime.Coordinator.LoadCreationSkills().Value!;
            page = new CreationSkillsPage(runtime.Coordinator, state);
            _ = new Window(new NavigationPage(page));
            await ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing"));
            await FindCardAsync();
            Require(Chosen().SpecializationOptionId == source.Specializations.Last().OptionId
                && Picker().SelectedIndex == source.Specializations.Count && !Choose().IsEnabled,
                "Fresh page did not restore the saved last specialization by ID.");
            var departedPicker = Picker();
            var departedChoose = Choose();
            IssuedPageLifecycle(page, "OnDisappearing");
            await ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing"));
            departedPicker.SelectedIndex = 0;
            await ClickAsync(departedChoose);
            Require(Chosen().SpecializationOptionId == source.Specializations.Last().OptionId,
                "A prior page appearance revived a retained specialization action.");
            var ownerPicker = Picker();
            var ownerChoose = Choose();
            string ownerDraft = JsonSerializer.Serialize(Draft().Skills);
            owners.Set(ContactsOwnerB);
            owners.Set(OwnerScope.LocalSingleUser);
            ownerPicker.SelectedIndex = 0;
            await ClickAsync(ownerChoose);
            Require(JsonSerializer.Serialize(Draft().Skills) == ownerDraft
                && FinalizationDocumentDigest(readSaved()) == FinalizationDocumentDigest(saved),
                "Owner A→B→A revived a retained specialization or changed the saved runner.");
            IssuedPageLifecycle(page, "OnDisappearing");
            ui.AssertHealthy();
        });
        Console.WriteLine($"PASS Skills picker/review: all {source.Specializations.Count} options for {source.Name}, EN/DE/ES readable review, collapsed exact diagnostics, explicit save/receipt/reopen, no implicit mutation, explicit removal, stale render/appearance/owner rejection");
    }

    internal static async Task RunCreationSkillsReviewFeedbackAsync(string contentRoot)
    {
        await using var runtime = new NativeRewardRuntime(contentRoot);
        var blockersField = typeof(CreationSkillsPage).GetField("_blockers",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var addReview = typeof(CreationSkillsPage).GetMethod("AddReview",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        foreach (bool blocked in new[] { true, false })
        {
            var page = new CreationSkillsPage(runtime.Coordinator);
            blockersField.SetValue(page, blocked
                ? new[] { CharacterCreationSkillsBlockers.NativeLanguageRequired }
                : Array.Empty<string>());
            // Rendering does not invoke the captured review callback or need a
            // newly granted mutation authority.
            addReview.Invoke(page, new object?[] { null });
            var body = (VerticalStackLayout)((ScrollView)page.Content!).Content!;
            Require(body.Children.Count == (blocked ? 2 : 1)
                && body.Children.Last() is Button { AutomationId: "creation-skills-review" },
                "The review action lost its immediately adjacent validation feedback.");
            if (blocked)
            {
                Require(body.Children[0] is Border { AutomationId: "creation-skills-review-blockers",
                    Content: VerticalStackLayout }, "Missing Skills validation notice beside Review.");
                var content = (VerticalStackLayout)((Border)body.Children[0]).Content!;
                Require(content.Children.OfType<Label>().Any(label =>
                    label.Text.Contains("Choose a native language", StringComparison.Ordinal)
                    && label.Text.Contains("not saved yet", StringComparison.Ordinal)),
                    "Review did not explain the missing native language and unsaved draft.");
            }
        }
        Console.WriteLine("PASS Skills review: missing-language explanation beside action, absent when unblocked");
    }

    internal static async Task RunCreationNativeLanguageEntryAsync(string contentRoot)
    {
        var owners = new ControlledLinkedOwner();
        await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners,
            creationFinalization: true, creationAttributes: true, creationSkills: true,
            productionCreationOverview: true);
        var saved = PrepareActualFinalizationReadyContext(runtime, stopBeforeSkills: true);
        await HydrateFinalizationOwnerAsync(runtime, owners, saved);
        var state = runtime.Coordinator.LoadCreationSkills().Value!;
        var options = state.Authority.KnowledgeSkills.Where(item => item.CanBeNativeLanguage).ToArray();
        Require(options.Length > 1, "Native-language test needs the real multi-language catalog.");
        var readSaved = () => new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!;
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            var page = new CreationSkillsPage(runtime.Coordinator, state);
            var navigation = new NavigationPage(page);
            _ = new Window(navigation);
            await ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing"));
            CreationSkillsPhoneDraft Draft() => (CreationSkillsPhoneDraft)typeof(CreationSkillsPage)
                .GetField("_draft", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(page)!;
            Picker Picker() => MinimalVisible(page).OfType<Picker>().Single(item =>
                item.AutomationId == "creation-skills-native-language-picker");
            Button Choose() => MinimalVisible(page).OfType<Button>().Single(item =>
                item.AutomationId == "creation-skills-native-language-preview");
            async Task ClickAsync(Button button) => await ui.BeginAsyncVoid(() => ((IButtonController)button).SendClicked());
            var picker = Picker();
            var choose = Choose();
            var body = (VerticalStackLayout)((ScrollView)page.Content!).Content!;
            var entry = body.Children.Single(item => item is Border { AutomationId: "creation-skills-native-language" });
            var budget = body.Children.Single(item => item is Border { AutomationId: "creation-skills-budget-active" });
            Require(body.Children.IndexOf(entry) < body.Children.IndexOf(budget)
                && picker.ItemsSource.Cast<string>().SequenceEqual(options.Select(item => item.Name))
                && picker.SelectedIndex == -1 && !choose.IsEnabled && picker.TextColor == NativeTheme.Text,
                "The first language is buried, guessed, truncated or unreadable.");
            string untouched = JsonSerializer.Serialize(Draft().Skills);
            picker.SelectedIndex = options.Length - 1;
            Require(choose.IsEnabled && JsonSerializer.Serialize(Draft().Skills) == untouched,
                "Selecting a picker item changed the draft without an explicit action.");
            // A control from a departed appearance must not select a language.
            IssuedPageLifecycle(page, "OnDisappearing");
            await ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing"));
            await ClickAsync(choose);
            Require(JsonSerializer.Serialize(Draft().Skills) == untouched,
                "A departed native-language action changed the draft.");
            picker = Picker(); choose = Choose();
            picker.SelectedIndex = options.Length - 1;
            await ClickAsync(choose);
            Require(Draft().Skills.Single(item => item.IsNativeLanguage).SourceSkillId == options.Last().SourceSkillId
                && !MinimalVisible(page).OfType<Picker>().Any(item => item.AutomationId == "creation-skills-native-language-picker")
                && MinimalVisibleText(page).Contains(options.Last().Name, StringComparison.Ordinal),
                "The full catalog choice did not become a readable Core-previewed native language.");
            string adopted = JsonSerializer.Serialize(Draft().Skills);
            picker.SelectedIndex = 0;
            await ClickAsync(choose);
            Require(JsonSerializer.Serialize(Draft().Skills) == adopted,
                "An earlier render replaced the chosen language.");
            await ClickAsync(MinimalVisible(page).OfType<Button>().Single(item =>
                item.AutomationId == "creation-skills-review-top"));
            Require(navigation.Navigation.NavigationStack.Last() is CreationSkillsPreviewPage
                && FinalizationDocumentDigest(readSaved()) == FinalizationDocumentDigest(saved),
                "Top review skipped the explicit preview or persisted choices implicitly.");
            IssuedPageLifecycle(page, "OnDisappearing");
            var ownerPage = new CreationSkillsPage(runtime.Coordinator, state);
            _ = new Window(new NavigationPage(ownerPage));
            page = ownerPage;
            await ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing"));
            picker = Picker(); choose = Choose();
            picker.SelectedIndex = 0;
            untouched = JsonSerializer.Serialize(Draft().Skills);
            owners.Set(ContactsOwnerB); owners.Set(OwnerScope.LocalSingleUser);
            await ClickAsync(choose);
            Require(JsonSerializer.Serialize(Draft().Skills) == untouched
                && FinalizationDocumentDigest(readSaved()) == FinalizationDocumentDigest(saved),
                "Owner A→B→A revived a native-language action.");
            IssuedPageLifecycle(page, "OnDisappearing");
            ui.AssertHealthy();
        });
        Console.WriteLine("PASS first native language: visible before budgets, full Core catalog, explicit preview, top review, no writes, stale render/appearance/owner rejection");
    }

    internal static async Task RunCreationFinalReviewNamesAsync(string contentRoot)
    {
        var legacy = new CharacterCreationFinalizationDelta(1, "skill:active:exact-id",
            CharacterCreationFinalizationDeltaKinds.Skill, "exact-id", null, "2", 0, 0, []);
        Require(CreationFinalizationPage.TargetLabel(legacy) == "exact-id",
            "Historical review without a name must retain its exact identity, not guess a catalog match.");
        Require(CreationFinalizationPage.TargetLabel(legacy with { TargetName = "  " }) == "exact-id",
            "An empty display name hid the exact fallback identity.");
        var named = legacy with { TargetName = "Pistols (Semi-Automatics)" };
        string before = JsonSerializer.Serialize(named);
        Require(CreationFinalizationPage.TargetLabel(named) == "Pistols (Semi-Automatics)"
            && JsonSerializer.Serialize(named) == before,
            "Display must preserve the admitted name, specialization and immutable delta bytes.");
        // The named path needs no client catalog lookup, including Life Modules.
        Require(LifeModuleCompletionPage.ReviewChange(null!, named) == "Pistols (Semi-Automatics): 2",
            "Life Modules ignored the canonical name or tried to reconstruct it from another catalog.");
        var language = named with { TargetName = "Salish", AfterValue = "native" };
        string nativeLanguage = LifeModuleCompletionPage.ReviewChange(null!, language);
        Require(nativeLanguage.StartsWith("Salish: ", StringComparison.Ordinal)
            && !nativeLanguage.EndsWith(": native", StringComparison.Ordinal),
            "Named language lost its readable native-language value.");
        Require(CreationFinalizationPage.ChangeLabel(language).EndsWith(CreationKarmaCopy.NativeLanguage, StringComparison.Ordinal),
            "Final review exposed the internal native-language marker.");
        var lifecycle = legacy with { Kind = CharacterCreationFinalizationDeltaKinds.Lifecycle,
            TargetId = "created", BeforeValue = "False", AfterValue = "True" };
        string lifecycleBytes = JsonSerializer.Serialize(lifecycle);
        var culture = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            foreach (string languageTag in new[] { "en", "de", "es" })
            {
                System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(languageTag);
                Require(CreationFinalizationPage.TargetLabel(lifecycle) != "created"
                    && CreationFinalizationPage.ChangeLabel(lifecycle) == CreationAllocationStrings.Get("Finalization.CareerTransition", "Creation → Career"),
                    "The admitted lifecycle transition needs readable localized copy.");
                Require(CreationFinalizationPage.ChangeLabel(lifecycle with { BeforeValue = "True" }) == "True → True",
                    "An unrecognized lifecycle transition was disguised as entering Career.");
                Require(CreationFinalizationReceiptPage.BuildMethodLabel(CharacterCreationBuildMethods.SumToTen)
                    == CreationAllocationStrings.Get("Finalization.Method.SumToTen", "Sum-to-Ten"),
                    "Receipt method localization must use the canonical contract value, not a guessed enum spelling.");
            }
        }
        finally { System.Globalization.CultureInfo.CurrentUICulture = culture; }
        Require(JsonSerializer.Serialize(lifecycle) == lifecycleBytes, "Lifecycle display mutated the exact Core delta.");
        await RunCreationFinalizationLocalBaselineAsync(contentRoot);
        Console.WriteLine("PASS final-review names: canonical label, historical fallback, Life Modules, real Core/MAUI render and cold receipt");
    }

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
        CloneFinalizationRecordFixture(runtime, ContactsOwnerA, before);
        owners.Set(ContactsOwnerA);
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
            var preparationWatch = System.Diagnostics.Stopwatch.StartNew();
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
            preparationWatch.Stop();
            probe.BeforeRead = null;
            var renderWatch = System.Diagnostics.Stopwatch.StartNew();
            refresh.Invoke(page, null);
            refresh.Invoke(page, null);
            renderWatch.Stop();
            Console.WriteLine($"MAGIC_TIMING initialPreparationMs={preparationWatch.Elapsed.TotalMilliseconds:F2} coreLoadMs={probe.LastLoadMilliseconds:F2} twoRendersMs={renderWatch.Elapsed.TotalMilliseconds:F2}");
            Require(probe.Loads == 1, "Rendering reloaded the entire Magic catalog.");
            var retainedEditor = (CharacterCreationMagicResonanceEditorState)typeof(CreationMagicResonancePage)
                .GetField("_editor", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(page)!;
            var retainedDraft = (CreationMagicResonancePhoneDraft)typeof(CreationMagicResonancePage)
                .GetField("_draft", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(page)!;
            if (retainedEditor.Talent.RequiresTradition)
            {
                foreach (var tradition in retainedEditor.Traditions.Where(item => item.IsEnabled && item.Blockers.Count == 0))
                    Require(CreationMagicResonancePhoneAuthority.IsOptionConfigurable(retainedEditor, tradition),
                        "Core-enabled tradition was rejected by the phone: " + tradition.Name);
                // Exercise real source-defined Possession/Inhabitation choices as
                // well as the absent-field Materialization defaults. Core still
                // owns admission; do not manufacture an enabled phone snapshot.
                foreach (string name in new[] { "Hermetic", "Shamanic", "Qabbalism", "Vodou",
                    "Insect Shaman", "Egyptian", "Psionic", "Santeria", "Svetoid" })
                {
                    var tradition = retainedEditor.Traditions.Single(item => item.Name == name);
                    Require(tradition.IsEnabled && tradition.Blockers.Count == 0,
                        "Source-admitted spirit-form tradition is unavailable: " + name);
                    var choicePage = new CreationMagicResonanceOptionPage(runtime.Coordinator, retainedEditor, tradition, retainedDraft);
                    await (Task)typeof(CreationMagicResonanceOptionPage).GetMethod("PrepareForAppearanceRefreshAsync",
                        BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(choicePage, [CancellationToken.None])!;
                    MinimalRender(choicePage);
                    Require(MinimalVisible(choicePage).OfType<Button>().Single(button =>
                        button.AutomationId == "creation-magic-resonance-option-toggle").IsEnabled,
                        "Actual native tradition page disabled " + name);
                }
            }
            foreach (string message in retainedEditor.Budgets.SelectMany(budget => budget.Blockers)
                         .Concat(retainedEditor.Blockers).Concat(retainedEditor.Talent.Blockers)
                         .Select(CreationFlowStrings.MagicBlocker).Distinct(StringComparer.Ordinal))
                Require(MinimalVisible(page).OfType<Label>().Count(label =>
                    label.Text.TrimStart('•', ' ') == message) == 1,
                    "Magic repeats or loses the same budget/talent guidance after refresh.");
            MinimalRequireNoMachineValues(page);
            MinimalRequireFreshDisclosure(page);
            var retainedCatalog = await VerifyMagicCatalogPagingAsync(runtime.Coordinator, retainedEditor, retainedDraft, ui);
            Require(probe.Loads == 1 && probe.Previews == 0 && probe.Confirms == 0,
                "Magic browsing must use the accepted snapshot without additional Core reads, previews or writes.");
            var option = new CreationMagicResonanceOptionPage(runtime.Coordinator, retainedEditor,
                retainedEditor.AdeptPowers.First(item => item.IsEnabled), retainedDraft);
            await (Task)typeof(CreationMagicResonanceOptionPage)
                .GetMethod("PrepareForAppearanceRefreshAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(option, [CancellationToken.None])!;
            MinimalRender(option);
            MinimalRequireNoMachineValues(option);
            var body = (VerticalStackLayout)((ScrollView)page.Content!).Content!;
            Require(body.Children.OfType<Button>().Any(button => button.AutomationId == "creation-magic-resonance-open-review" && button.IsEnabled),
                "Background catalog did not expose the actual Core-ready editor.");
            var review = await runtime.Coordinator.ReviewCreationMagicResonanceForDisplayAsync(original, original.CreationMagicResonanceEditor!, draft);
            Require(review.Preview.CanConfirm && probe.Previews == 1 && owners.ActiveLeases == 0,
                "Background preview lost authority or retained an owner lease.");
            var returnedDraft = retainedDraft.Copy();
            Require(returnedDraft.TryAdopt(retainedEditor, original, review),
                "Return-navigation regression fixture could not adopt the exact Core review.");
            var selectedBeforeReturn = returnedDraft.Selections;
            var returnWatch = System.Diagnostics.Stopwatch.StartNew();
            Require(returnedDraft.TryBindLoaded(original.CreationMagicResonance!, original, out var returnedEditor)
                && returnedEditor is not null && ReferenceEquals(selectedBeforeReturn, returnedDraft.Selections)
                && ReferenceEquals(review, returnedDraft.Review) && returnedDraft.Matches(returnedEditor, original),
                "Fresh return admission discarded unsaved selections or their exact review.");
            Console.WriteLine($"MAGIC_TIMING draftReturnWithIndependentCheckMs={returnWatch.Elapsed.TotalMilliseconds:F2}");
            var journal = CharacterCreationMagicResonanceCheckpointStore.CreateDefault(
                original.DisplayOwnerContext, runtime.Coordinator.IsCreationMagicOwnerCurrent, id.Value);
            Require(journal.TryCreate(CharacterCreationMagicResonanceCheckpoint.CreateReviewed(review),
                out var storedReview, out _), "Scoped Magic review was not durable.");
            var readableReview = new CreationMagicResonanceReviewPage(runtime.Coordinator, storedReview, journal);
            MinimalRender(readableReview);
            MinimalRequireNoMachineValues(readableReview);
            MinimalRequireFreshDisclosure(readableReview);
            foreach (var identity in review.Preview.Selections.AdeptPowers.Select(item => item.Identity)
                         .Concat(review.Preview.Selections.Spells))
            {
                string name = retainedEditor.AdeptPowers.Concat(retainedEditor.Spells)
                    .Single(item => item.Identity == identity).Name;
                Require(MinimalVisibleText(readableReview).Contains(name),
                    "Magic review must show the exact catalog name rather than an ID or placeholder.");
            }
            Require(!CharacterCreationMagicResonanceCheckpointStore.CreateDefault().TryRead(out _, out var localBlocker)
                && string.IsNullOrEmpty(localBlocker), "A scoped review leaked into the legacy local journal.");
            await VerifyMagicConfirmationFeedbackAsync(runtime.Coordinator, probe, journal, storedReview);
            await VerifyMagicOptionPendingAsync(runtime.Coordinator, probe, retainedEditor, retainedDraft, draft, ui);

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
                var otherStamp = owners.Capture();
                var otherJournal = CharacterCreationMagicResonanceCheckpointStore.CreateDefault(
                    otherStamp, stamp => stamp == owners.Capture(), id.Value);
                Require(!otherJournal.TryRead(out _, out var otherBlocker) && string.IsNullOrEmpty(otherBlocker),
                    "Another account could read the Magic review.");
                owners.Set(ContactsOwnerA);
            }
            finally { gate.Release(); }
            try { await stale; throw new Exception("Owner ABA accepted an old Magic preview."); }
            catch (InvalidOperationException error) when (error.Message == CharacterCreationMagicResonanceBlockers.StaleWorkspaceRevision) { }
            Require(probe.Loads == reads && owners.ActiveLeases == 0, "Stale Magic worker entered Core.");
            int staleNavigationCount = retainedCatalog.Navigation.Navigation.NavigationStack.Count;
            await ui.BeginAsyncVoid(() => ((IButtonController)retainedCatalog.Row).SendClicked());
            retainedCatalog.Search.Text = "invisible-owner-search";
            ((ISearchBarController)retainedCatalog.Search).OnSearchButtonPressed();
            Require(retainedCatalog.Navigation.Navigation.NavigationStack.Count == staleNavigationCount,
                "An old catalog row navigated after owner A→B→A.");
            MinimalRender(retainedCatalog.Page);
            Require(!MinimalVisible(retainedCatalog.Page).OfType<Button>().Any(button =>
                    button.AutomationId?.StartsWith("creation-magic-resonance-option-", StringComparison.Ordinal) == true),
                "An old owner retained visible catalog actions.");
            IssuedPageLifecycle(retainedCatalog.Page, "OnDisappearing");
            refresh.Invoke(page, null);
            Require(!body.Children.OfType<Button>().Any(button => button.AutomationId == "creation-magic-resonance-open-review"),
                "Owner transition left the old Magic catalog actionable.");
            Require(!journal.TryRead(out _, out var staleBlocker) && !string.IsNullOrEmpty(staleBlocker),
                "Old owner epoch retained journal access.");

            await HydrateFinalizationOwnerAsync(runtime, owners, before);
            var fresh = runtime.Coordinator.State;
            Require(!retainedDraft.Matches(retainedEditor, fresh)
                && !retainedDraft.Copy().Matches(retainedEditor, fresh),
                "Old Magic selections survived an owner epoch change as current authority.");
            Require(returnedDraft.TryBindLoaded(fresh.CreationMagicResonance!, fresh, out var freshEditor)
                && freshEditor is not null && returnedDraft.Review is null
                && CharacterCreationMagicResonanceDigest.Compute(returnedDraft.Selections)
                    == CharacterCreationMagicResonanceDigest.Compute(freshEditor.Selections),
                "Fresh owner A after A→B→A inherited an earlier epoch's unsaved choices or review.");
            var freshJournal = CharacterCreationMagicResonanceCheckpointStore.CreateDefault(
                fresh.DisplayOwnerContext, runtime.Coordinator.IsCreationMagicOwnerCurrent, id.Value);
            Require(freshJournal.TryRead(out var recovered, out _)
                && recovered.CheckpointDigest == storedReview.CheckpointDigest,
                "Fresh same-account authority lost its durable review.");
            Require(freshJournal.TryBeginConfirm(CharacterCreationMagicResonanceCheckpointCas.From(recovered),
                out var confirming, out _), "Magic confirmation was not journaled.");
            // A detached button from A before A→B→A must not rebind its command to fresh A.
            int beforeRecoveryReads = probe.Loads;
            var resolve = typeof(CreationMagicResonancePage).GetMethod("ResolveConfirmingAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
            await (Task)resolve.Invoke(page, [confirming, original])!;
            Require(probe.Confirms == 0 && probe.Loads == beforeRecoveryReads
                && freshJournal.TryRead(out var unchangedConfirming, out _)
                && unchangedConfirming.CheckpointDigest == confirming.CheckpointDigest
                && new FileWorkspaceStore(runtime.StateDirectory).Get(ContactsOwnerA, id).Value!.ContentRevision == before.ContentRevision,
                "Stale rendered recovery rebound to a fresh owner epoch or changed its durable command.");
            var stalePreview = typeof(CreationMagicResonancePage).GetMethod("PreviewDraftAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
            try
            {
                await (Task)stalePreview.Invoke(page, [retainedEditor, draft])!;
                throw new Exception("Stale rendered selection rebound to current owner authority.");
            }
            catch (InvalidOperationException error) when (error.Message == CharacterCreationMagicResonanceBlockers.StaleWorkspaceRevision) { }
            try
            {
                await (Task)typeof(CreationMagicResonanceOptionPage)
                    .GetMethod("AdoptAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(option, [draft])!;
                throw new Exception("Stale rendered option rebound to current owner authority.");
            }
            catch (InvalidOperationException error) when (error.Message == CharacterCreationMagicResonanceBlockers.StaleWorkspaceRevision) { }
            Require(probe.Loads == beforeRecoveryReads, "Stale rendered selection entered Core.");
            probe.AllowConfirm = true;
            var confirmed = await runtime.Coordinator.ConfirmCreationMagicResonanceAsync(confirming, display: fresh);
            Require(confirmed.MutationOutcomeKnown && confirmed.Outcome == CreationMagicResonancePhoneOutcomes.Applied
                && confirmed.Confirmation is not null && probe.Confirms == 1, "Owner-bound native Magic confirmation failed.");
            Require(freshJournal.TryRecordConfirmed(CharacterCreationMagicResonanceCheckpointCas.From(confirming),
                confirmed.Confirmation!, out var savedCheckpoint, out _), "Owner-bound Magic receipt was not journaled.");
            var cold = new FileWorkspaceStore(runtime.StateDirectory).Get(ContactsOwnerA, id).Value!;
            Require(cold.ContentRevision == before.ContentRevision + 1 && cold.SavedRevision == cold.ContentRevision
                && cold.Document.Content == before.Document.Content
                && runtime.Coordinator.State.CreationMagicResonance?.PendingDraft is not null,
                "Native confirmation did not persist and re-project exactly one scoped draft.");
            var receiptPage = new CreationMagicResonanceReceiptPage(runtime.Coordinator,
                savedCheckpoint, confirmed.Confirmation!, freshJournal);
            var originalCulture = System.Globalization.CultureInfo.CurrentUICulture;
            try
            {
                foreach (string locale in new[] { "en-GB", "de-AT", "es-MX" })
                {
                    System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(locale);
                    MinimalRender(receiptPage);
                    MinimalRequireNoMachineValues(receiptPage);
                    string text = MinimalVisibleText(receiptPage);
                    Require(text.Contains(CreationMagicResonancePage.KindLabel(confirmed.Confirmation!.Receipt.TalentKind))
                        && text.Contains(CreationFlowStrings.Get("Magic.Receipt.Safe", "missing")),
                        "Saved Magic choices lost their localized talent or useful save guidance.");
                    foreach (string diagnostic in new[] { "Common.PreviousRevision", "Common.ContentRevision", "Common.SavedRevision",
                        "Common.DraftRevision", "Magic.Receipt.IdempotentReplay", "Magic.Receipt.CurrentDraft", "Common.DocumentChanged" })
                        Require(!text.Contains(CreationFlowStrings.Get(diagnostic, "missing")),
                            "Saved Magic diagnostics still dominate the ordinary receipt: " + diagnostic);
                    var toggle = MinimalVisible(receiptPage).OfType<Button>()
                        .Single(button => button.AutomationId == "creation-magic-resonance-receipt-details-toggle");
                    ((IButtonController)toggle).SendClicked();
                    Require(MinimalVisibleText(receiptPage).Contains(confirmed.Confirmation.Receipt.ReceiptDigest),
                        "Expanded Magic diagnostics lost the exact receipt.");
                    ((IButtonController)toggle).SendClicked();
                    MinimalRequireNoMachineValues(receiptPage);
                }
            }
            finally { System.Globalization.CultureInfo.CurrentUICulture = originalCulture; }
            Require(freshJournal.TryRead(out var retainedReceipt, out _)
                && retainedReceipt.CheckpointDigest == savedCheckpoint.CheckpointDigest,
                "Rendering the Magic receipt acknowledged or changed the durable save.");
            foreach (var owner in new[] { ContactsOwnerB, ContactsOwnerA })
            {
                owners.Set(owner);
                MinimalRender(receiptPage);
                Require(!MinimalVisible(receiptPage).OfType<Button>().Any(),
                    "A retained Magic receipt exposes actions or diagnostics after an owner transition.");
                await (Task)typeof(CreationMagicResonanceReceiptPage)
                    .GetMethod("AcknowledgeAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(receiptPage, null)!;
            }
            await HydrateFinalizationOwnerAsync(runtime, owners, cold);
            var reopenedJournal = CharacterCreationMagicResonanceCheckpointStore.CreateDefault(
                runtime.Coordinator.State.DisplayOwnerContext, runtime.Coordinator.IsCreationMagicOwnerCurrent, id.Value);
            Require(reopenedJournal.TryRead(out var afterOwnerSwitch, out _)
                && afterOwnerSwitch.CheckpointDigest == savedCheckpoint.CheckpointDigest,
                "A stale receipt button acknowledged the durable save across owner transitions.");
            var recoveredPage = new CreationMagicResonanceReceiptPage(runtime.Coordinator,
                afterOwnerSwitch, afterOwnerSwitch.Confirmation!, reopenedJournal);
            MinimalRender(recoveredPage);
            Require(MinimalVisible(recoveredPage).OfType<Button>().Single(button =>
                button.AutomationId == "creation-magic-resonance-receipt-acknowledge").IsEnabled,
                "Returning to the original account with a fresh owner stamp cannot recover its saved receipt.");
            MinimalRequireNoMachineValues(recoveredPage);
            RequireSameRewardDocument(cold, new FileWorkspaceStore(runtime.StateDirectory).Get(ContactsOwnerA, id).Value!);
        });
        RequireSameRewardDocument(before, store.Get(id).Value!);
        Console.WriteLine("PASS Magic scoped catalog/preview/confirm, UI heartbeat, cancellation, owner ABA, journal isolation/recovery and unchanged local workspace");
    }

    private static async Task<(CreationMagicResonanceCatalogPage Page, Button Row, SearchBar Search, NavigationPage Navigation)>
        VerifyMagicCatalogPagingAsync(RunnerSessionCoordinator coordinator,
        CharacterCreationMagicResonanceEditorState editor, CreationMagicResonancePhoneDraft draft, IssuedPageUiContext ui)
    {
        foreach (string locale in new[] { "en-GB", "de-AT", "es-MX" })
        foreach (string key in new[] { "Search", "NoMatches", "Showing", "Previous", "Next" })
            Require(CreationFlowStrings.Get("Magic.Catalog." + key, "missing",
                    System.Globalization.CultureInfo.GetCultureInfo(locale)) != "missing", "Missing Magic search translation.");
        var allOptions = editor.Spells.Count > 20 ? editor.Spells : editor.AdeptPowers;
        var options = allOptions.Where(option => option.IsEnabled && option.Blockers.Count == 0
            || draft.IsSelected(option.Identity)).ToArray();
        Require(options.Length > 20, "SETUP: the actual Magic catalog must exercise multiple pages.");
        string beforeSelections = JsonSerializer.Serialize(draft.Selections);
        var catalog = new CreationMagicResonanceCatalogPage(coordinator, editor,
            options[0].Identity.Kind, allOptions, draft);
        var navigation = new NavigationPage(catalog);
        _ = new Window(navigation);
        await ui.BeginAsyncVoid(() => IssuedPageLifecycle(catalog, "OnAppearing"));
        Button[] Rows() => MinimalVisible(catalog).OfType<Button>().Where(button =>
            button.AutomationId?.StartsWith("creation-magic-resonance-option-", StringComparison.Ordinal) == true).ToArray();
        Button Pager(string direction) => MinimalVisible(catalog).OfType<Button>().Single(button =>
            button.AutomationId == "creation-magic-resonance-catalog-" + direction);
        string Id(CharacterCreationMagicResonanceOptionProjection option) =>
            $"creation-magic-resonance-option-{CreationMagicResonancePage.Token(option.Identity.Kind)}-{CreationMagicResonancePage.Token(option.Identity.SourceId)}";
        Require(Rows().Length == 20, $"Magic must render a bounded catalog page, not all {options.Length} rows (rendered {Rows().Length}).");
        var disabled = allOptions.First(item => item.Blockers.Count > 0);
        var oldCulture = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            foreach (string locale in new[] { "en-GB", "de-AT", "es-MX" })
            {
                System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(locale);
                string fallback = CreationFlowStrings.Get("Magic.Blocker.Unknown", "missing");
                foreach (var field in typeof(CharacterCreationMagicResonanceBlockers).GetFields(BindingFlags.Public | BindingFlags.Static)
                    .Concat(typeof(CreationMagicResonancePhoneBlockers).GetFields(BindingFlags.Public | BindingFlags.Static)))
                {
                    string code = (string)field.GetRawConstantValue()!;
                    string message = CreationFlowStrings.MagicBlocker(code);
                    Require(message != fallback && message != code && !message.Contains("creation-magic-resonance-"),
                        "A current Magic blocker lost its specific explanation: " + code + "/" + locale);
                }
                Require(CreationFlowStrings.MagicBlocker("future-error: 7bc6d833-dca3-451c-a21c-956af80f4eb7") == fallback,
                    "Unknown errors must not leak exception text or IDs into ordinary guidance.");
                var detail = new CreationMagicResonanceOptionPage(coordinator, editor, disabled, draft);
                await (Task)typeof(CreationMagicResonanceOptionPage)
                    .GetMethod("PrepareForAppearanceRefreshAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .Invoke(detail, [CancellationToken.None])!;
                MinimalRender(detail);
                Require(!MinimalVisibleText(detail).Contains("creation-magic-resonance-", StringComparison.Ordinal),
                    "A disabled Magic choice exposes raw blocker codes instead of readable guidance: " + locale);
                Require(disabled.Blockers.All(code => MinimalVisibleText(detail).Contains(CreationFlowStrings.MagicCatalogBlocker(code))),
                    "Disabled choices need the exact localized reason, not a blank or generic summary.");
                foreach (string message in disabled.Blockers.Select(CreationFlowStrings.MagicCatalogBlocker).Distinct(StringComparer.Ordinal))
                    Require(MinimalVisible(detail).OfType<Label>().Count(label =>
                        label.Text.TrimStart('•', ' ') == message) == 1,
                        "A blocked Magic choice repeats the same guidance: " + locale);
                Require(!MinimalVisible(detail).OfType<Button>().Single(button =>
                    button.AutomationId == "creation-magic-resonance-option-toggle"
                    || button.AutomationId == "creation-magic-resonance-power-increase").IsEnabled,
                    "Readable guidance must not enable a blocked Core option.");
                var panel = MinimalVisible(detail).OfType<VerticalStackLayout>().Single(item =>
                    item.AutomationId == "creation-magic-resonance-option-details");
                var toggle = panel.Children.OfType<Button>().Single();
                ((IButtonController)toggle).SendClicked();
                Require(disabled.Blockers.All(code => MinimalVisibleText(detail).Contains(code, StringComparison.Ordinal)),
                    "Expanded diagnostics must retain the exact Core blocker codes.");
                ((IButtonController)toggle).SendClicked();
                Require(!MinimalVisibleText(detail).Contains("creation-magic-resonance-", StringComparison.Ordinal),
                    "Closing diagnostics left raw codes in the ordinary UI.");
            }
        }
        finally { System.Globalization.CultureInfo.CurrentUICulture = oldCulture; }
        var sharedHints = new VerticalStackLayout();
        var exactCodes = new VerticalStackLayout();
        var shown = new HashSet<string>(StringComparer.Ordinal);
        string[] codes = [CharacterCreationMagicResonanceBlockers.OptionInvalid,
            CharacterCreationMagicResonanceBlockers.OptionDisabled,
            CharacterCreationMagicResonanceBlockers.TraditionRequired];
        foreach (string code in codes.Concat(codes))
            CreationMagicResonancePage.AddBlocker(sharedHints, code, exactCodes, shown);
        Require(sharedHints.Children.Count == 2 && exactCodes.Children.Count == 3
            && codes.All(code => exactCodes.Children.OfType<Label>().Any(label => label.Text == code)),
            "Shared readable guidance must retain every distinct exact code without repeating hints.");
        foreach (Button button in new[] { NativeTheme.PrimaryButton("Select"), NativeTheme.SecondaryButton("Previous") })
        {
            var background = button.BackgroundColor;
            var foreground = button.TextColor;
            double height = button.HeightRequest;
            button.IsEnabled = false;
            Require(VisualStateManager.GoToState(button, "Disabled")
                && button.BackgroundColor.Equals(NativeTheme.Line)
                && button.TextColor.Equals(NativeTheme.Text)
                && button.BorderColor.Equals(NativeTheme.Muted)
                && button.Opacity == 1 && button.HeightRequest == height && !button.IsEnabled,
                "Disabled actions must look inactive without fading text or changing touch targets/admission.");
            button.IsEnabled = true;
            Require(VisualStateManager.GoToState(button, "Normal")
                && button.BackgroundColor.Equals(background) && button.TextColor.Equals(foreground),
                "Re-enabled actions did not recover their original appearance.");
            button.BackgroundColor = NativeTheme.Signal;
            button.TextColor = NativeTheme.Ink;
            button.IsEnabled = false;
            VisualStateManager.GoToState(button, "Disabled");
            button.IsEnabled = true;
            VisualStateManager.GoToState(button, "Normal");
            Require(button.BackgroundColor.Equals(NativeTheme.Signal) && button.TextColor.Equals(NativeTheme.Ink),
                "Availability states replaced a caller's custom enabled colors.");
        }
        var search = MinimalVisible(catalog).OfType<SearchBar>().Single();
        Require(search.TextColor.Equals(NativeTheme.Text) && search.PlaceholderColor.Equals(NativeTheme.Muted)
            && search.BackgroundColor.Equals(NativeTheme.Surface), "Magic search colors must remain readable.");
        string[] first = Rows().Select(button => button.AutomationId).ToArray();
        Button detachedNext = Pager("next"), detachedRow = Rows()[0];
        ((IButtonController)detachedNext).SendClicked();
        var second = Rows();
        ((IButtonController)detachedNext).SendClicked();
        Require(Rows().SequenceEqual(second), "A detached Next button advanced a newer page.");
        int stackCount = navigation.Navigation.NavigationStack.Count;
        await ui.BeginAsyncVoid(() => ((IButtonController)detachedRow).SendClicked());
        Require(navigation.Navigation.NavigationStack.Count == stackCount, "A detached row opened another page.");
        ((IButtonController)Pager("previous")).SendClicked();
        Require(Rows().Select(button => button.AutomationId).SequenceEqual(first), "Previous lost exact source order.");
        var visited = new List<string>();
        do
        {
            Require(Rows().Length <= 20, "A later catalog page exceeded the row bound.");
            visited.AddRange(Rows().Select(button => button.AutomationId));
            if (!Pager("next").IsEnabled) break;
            ((IButtonController)Pager("next")).SendClicked();
        } while (visited.Count <= options.Length);
        Require(visited.SequenceEqual(options.Select(Id)), "Paging omitted, repeated or reordered a Core option.");
        Require(!visited.Contains(Id(disabled)), "An unavailable choice was offered by default.");
        void Search(string query)
        {
            search.Text = query;
            ((ISearchBarController)search).OnSearchButtonPressed();
        }
        var supplements = allOptions.Where(item => CreationMagicNativeRuntimeTests.HasNewSupplementSummary(
            item.SourceBook, item.Category, item.Name)).ToArray();
        if (options[0].Identity.Kind == CharacterCreationMagicResonanceKinds.Spell)
        {
            Require(supplements.Length == 31 && supplements.Count(options.Contains) == 30,
                "The native catalog fixture must retain all 31 supplement definitions and 30 admitted choices.");
            var blocked = supplements.Single(item => !options.Contains(item));
            Require(blocked.SourceBook == "SG" && blocked.Name == "Clean [Element]"
                && blocked.Blockers.Contains(CharacterCreationMagicResonanceBlockers.OptionSemanticsUnsupported),
                "Adding help must not enable an unsupported parameterized spell or hide an ordinary choice.");
            var bloodSpells = allOptions.Where(item =>
                CreationMagicNativeRuntimeTests.HasBloodSpellSummary(item.SourceBook, item.Name)).ToArray();
            Require(bloodSpells.Length == 17 && bloodSpells.All(item => !options.Contains(item)),
                "Explanations must not admit blood spells into this ordinary Creation fixture.");
        }
        if (options[0].Identity.Kind == CharacterCreationMagicResonanceKinds.Spell)
        foreach (var spell in new[] { "Levitate", "Lightning Bolt", "Detect Enemies, Extended",
            "Antidote", "Detox", "Resist Pain", "Phantasm", "Trid Phantasm", "Silence",
            "Animate", "Mana Barrier", "Physical Barrier", "Awaken", "Fast", "Enabler", "Forced Defense",
            "Firewater", "Napalm", "Ice Spear", "Ice Storm", "Shattershield", "Diagnose", "Mana Window",
            "Astral Window", "Mindnet", "Mindnet Extended", "Night Vision", "Spatial Sense, Extended",
            "Thought Recognition", "Area Thought Recognition", "Translate" }
            .Select(name => options.Single(item => item.Name == name))
            .Concat(options.Where(item => item.SourceBook == "SG" && item.Category == "Illusion"))
            .Concat(supplements)
            .Concat(allOptions.Where(item => item.SourceBook == "SG" && item.Category == "Manipulation"))
            .Concat(allOptions.Where(item => item.SourceBook == "SSP" && item.Category != "Rituals"))
            .Concat(allOptions.Where(item => CreationMagicNativeRuntimeTests.HasOrdinaryArcanaSummary(item.SourceBook, item.Name)))
            .Concat(allOptions.Where(item => CreationMagicNativeRuntimeTests.HasBloodSpellSummary(item.SourceBook, item.Name)))
            .Distinct())
        {
            Search(spell.Name);
            string help = CreationSpellInfo.Summary(CreationSpellInfo.Resolve(coordinator.State.CreationMagicResonance, spell));
            if (options.Contains(spell))
                Require(MinimalVisibleText(catalog).Contains(help), "The actual spell list omitted its inline description.");
            else
                Require(!Rows().Any(row => row.AutomationId == Id(spell)),
                    "An explanatory summary made an unsupported spell selectable.");
            var spellPage = new CreationMagicResonanceOptionPage(coordinator, editor, spell, draft);
            await (Task)typeof(CreationMagicResonanceOptionPage).GetMethod("PrepareForAppearanceRefreshAsync",
                BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(spellPage, [CancellationToken.None])!;
            MinimalRender(spellPage);
            var summary = MinimalVisible(spellPage).OfType<Label>().Single(label =>
                label.AutomationId == "creation-magic-resonance-spell-summary");
            Require(summary.Text == help && summary.TextColor.Equals(NativeTheme.Text)
                && summary.LineBreakMode == LineBreakMode.WordWrap, "Spell details are missing, clipped or unreadable.");
            if (!options.Contains(spell))
                Require(!MinimalVisible(spellPage).OfType<Button>().Single(button =>
                    button.AutomationId == "creation-magic-resonance-option-toggle").IsEnabled,
                    "Help on an unsupported spell enabled its selection action.");
        }
        Search("  " + options.Last().Name.ToUpperInvariant() + "  ");
        Require(Rows().Select(button => button.AutomationId).Contains(Id(options.Last())),
            "Trimmed name search cannot find the final source option.");
        Search(options[0].SourceBook);
        Require(Rows().Select(button => button.AutomationId).SequenceEqual(options.Where(option =>
            option.Name.Contains(options[0].SourceBook, StringComparison.CurrentCultureIgnoreCase)
            || option.SourceBook.Contains(options[0].SourceBook, StringComparison.CurrentCultureIgnoreCase)).Take(20).Select(Id)),
            "Source-book search changed exact source identities.");
        Search("not-a-real-magic-choice-20261001");
        Require(Rows().Length == 0 && !Pager("next").IsEnabled && !Pager("previous").IsEnabled,
            "Empty search left stale choices or enabled paging.");
        search.Text = string.Empty;
        Require(Rows().Select(button => button.AutomationId).SequenceEqual(first), "Clearing search did not reset paging.");
        Require(ReferenceEquals(search, MinimalVisible(catalog).OfType<SearchBar>().Single()),
            "Paging/search replaced the focused search control.");
        MinimalRequireNoMachineValues(catalog);
        MinimalRender(catalog);
        Search(options.Last().Name);
        Require(Rows().Select(button => button.AutomationId).SequenceEqual(first), "A detached search changed a newer render.");
        Require(JsonSerializer.Serialize(draft.Selections) == beforeSelections, "Browsing edited the Magic draft.");
        Console.WriteLine($"PASS Magic catalog {options.Length} available exact options, bounded pages, search, readable colors, detached controls and unchanged draft");
        return (catalog, Rows()[0], MinimalVisible(catalog).OfType<SearchBar>().Single(), navigation);
    }

    private static async Task VerifyMagicOptionPendingAsync(RunnerSessionCoordinator coordinator,
        MagicReadProbe probe, CharacterCreationMagicResonanceEditorState editor,
        CreationMagicResonancePhoneDraft originalDraft, CharacterCreationMagicResonanceDesktopDraft candidate,
        IssuedPageUiContext ui)
    {
        foreach (string locale in new[] { "en-GB", "de-AT", "es-MX" })
            Require(CreationFlowStrings.Get("Magic.Option.Checking", "missing",
                System.Globalization.CultureInfo.GetCultureInfo(locale)) != "missing", "Missing pending choice translation.");
        var gate = (SemaphoreSlim)typeof(RunnerSessionCoordinator)
            .GetField("_workspaceActivationGate", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(coordinator)!;
        var adopt = typeof(CreationMagicResonanceOptionPage).GetMethod("AdoptAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var navigating = typeof(CreationMagicResonanceOptionPage).GetMethod("OnPendingNavigation", BindingFlags.NonPublic | BindingFlags.Instance)!;
        foreach (string outcome in new[] { "error", "cancel", "departure", "reappearance", "success" })
        {
            var draft = originalDraft.Copy();
            string initial = JsonSerializer.Serialize(draft.Selections);
            var page = new CreationMagicResonanceOptionPage(coordinator, editor,
                editor.Spells.Concat(editor.AdeptPowers).First(item =>
                    CreationMagicResonancePhoneAuthority.IsOptionConfigurable(editor, item)), draft);
            var shell = new Shell();
            var content = new ShellContent { Content = page };
            shell.Items.Add(content);
            _ = new Window(shell);
            await ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing"));
            Require(ReferenceEquals(shell.CurrentPage, page), "SETUP: option page is not the active Shell page.");
            var body = (VerticalStackLayout)((ScrollView)page.Content!).Content!;
            var rendered = body.Children.ToArray();
            var button = body.Children.OfType<Button>().Single(item =>
                item.AutomationId is "creation-magic-resonance-option-toggle" or "creation-magic-resonance-power-increase");
            var progress = body.Children.OfType<Label>().Single(item => item.AutomationId == "creation-magic-resonance-option-progress");
            Require(button.IsEnabled && !progress.IsVisible, "Fresh option controls are not ready.");
            probe.BeforeRead = outcome switch
            {
                "error" => () => throw new InvalidOperationException("magic-option-pending-test"),
                "cancel" => () => throw new OperationCanceledException("magic-option-pending-test"),
                _ => null
            };
            int previews = probe.Previews;
            await gate.WaitAsync();
            Task pending = (Task)adopt.Invoke(page, [candidate])!;
            Exception? assertion = null;
            try
            {
                Require(!pending.IsCompleted && !button.IsEnabled && progress.IsVisible
                    && progress.TextColor == NativeTheme.Text && !Shell.GetBackButtonBehavior(page).IsEnabled
                    && page.SendBackButtonPressed() && rendered.SequenceEqual(body.Children)
                    && !MinimalVisible(page).OfType<ActivityIndicator>().Any(item => item.IsRunning),
                    "Pending option must show static in-place feedback and block hardware/toolbar back before awaiting Core.");
                Require(ReferenceEquals(typeof(CreationMagicResonanceOptionPage)
                    .GetField("_pendingShell", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(page), shell),
                    "Pending choice did not subscribe to its owning Shell.");
                foreach (var (source, canCancel, expected) in new[] {
                    (ShellNavigationSource.Pop, true, true), (ShellNavigationSource.PopToRoot, true, true),
                    (ShellNavigationSource.Pop, false, false), (ShellNavigationSource.ShellItemChanged, true, false) })
                {
                    var args = new ShellNavigatingEventArgs(new ShellNavigationState("//runner/option"),
                        new ShellNavigationState("//runner"), source, canCancel);
                    navigating.Invoke(page, [shell, args]);
                    Require(args.Cancelled == expected, "Pending choice intercepted the wrong navigation kind.");
                }
                await (Task)adopt.Invoke(page, [candidate])!;
                Require(!pending.IsCompleted && probe.Previews == previews && progress.IsVisible && !button.IsEnabled,
                    "Duplicate choice started another preview or cleared pending feedback.");
                MinimalRender(page);
                Require(rendered.SequenceEqual(body.Children), "Pending refresh replaced the active choice controls.");
                if (outcome is "departure" or "reappearance")
                {
                    IssuedPageLifecycle(page, "OnDisappearing");
                    Require(Shell.GetBackButtonBehavior(page).IsEnabled, "Departure retained the back lock.");
                    if (outcome == "reappearance")
                        await ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing"));
                }
            }
            catch (Exception error) { assertion = error; }
            finally { gate.Release(); }
            try
            {
                await pending;
                Require(outcome == "success", "A canceled/failed/departed option was accepted.");
                Require(draft.Review is not null && JsonSerializer.Serialize(draft.Selections) != initial,
                    "Successful preview did not retain the exact selected draft.");
            }
            catch (OperationCanceledException) when (outcome is "cancel" or "departure" or "reappearance") { }
            catch (InvalidOperationException error) when (outcome == "error" && error.Message == "magic-option-pending-test") { }
            finally
            {
                probe.BeforeRead = null;
                if (outcome is not ("departure" or "reappearance")) Require(button.IsEnabled,
                    "Current choice controls did not recover after the pending operation.");
                if (outcome == "reappearance") Require(body.Children.OfType<Button>().Any(item =>
                    item.AutomationId == button.AutomationId && item.IsEnabled && !ReferenceEquals(item, button)),
                    "Old preview completion stranded a newly prepared appearance.");
                if (outcome != "departure") IssuedPageLifecycle(page, "OnDisappearing");
            }
            if (assertion is not null) throw assertion;
            Require(!progress.IsVisible && Shell.GetBackButtonBehavior(page).IsEnabled
                && typeof(CreationMagicResonanceOptionPage).GetField("_pendingShell", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(page) is null,
                "Completed/failed preview retained progress or trapped back navigation.");
            if (outcome != "success") Require(JsonSerializer.Serialize(draft.Selections) == initial,
                "Failed/departed preview changed the shared draft.");
            Require(probe.Confirms == 0, "Selecting a draft choice performed a durable confirmation.");
        }
        Console.WriteLine("PASS Magic option static pending feedback, duplicate/back guards, exact adoption and error/cancel/departure/reappearance cleanup");
    }

    private static async Task VerifyMagicConfirmationFeedbackAsync(RunnerSessionCoordinator coordinator,
        MagicReadProbe probe, CharacterCreationMagicResonanceCheckpointStore journal,
        CharacterCreationMagicResonanceCheckpoint reviewed)
    {
        var page = new CreationMagicResonanceReviewPage(coordinator, reviewed, journal);
        var refresh = typeof(CreationMagicResonanceReviewPage)
            .GetMethod("Refresh", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var confirm = typeof(CreationMagicResonanceReviewPage)
            .GetMethod("ConfirmAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var gate = (SemaphoreSlim)typeof(RunnerSessionCoordinator)
            .GetField("_workspaceActivationGate", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(coordinator)!;
        foreach (bool cancel in new[] { false, true })
        {
            refresh.Invoke(page, null);
            var body = (VerticalStackLayout)((ScrollView)page.Content!).Content!;
            var button = body.Children.OfType<Button>().Single(item => item.AutomationId == "creation-magic-resonance-confirm-draft");
            string idleText = button.Text;
            var rendered = body.Children.ToArray();
            Require(button.IsEnabled, "SETUP: exact Magic review must allow confirmation.");
            int reads = probe.Loads;
            probe.BeforeRead = () =>
            {
                if (cancel) throw new OperationCanceledException("magic-feedback-test");
                throw new InvalidOperationException("magic-feedback-test");
            };
            await gate.WaitAsync();
            Task pending = (Task)confirm.Invoke(page, null)!;
            Exception? assertion = null;
            try
            {
                Require(!pending.IsCompleted && !button.IsEnabled && button.Text != idleText,
                    "Magic confirmation must immediately disable and relabel the rendered button before awaiting Core.");
                var progress = body.Children.OfType<Label>()
                    .Single(item => item.AutomationId == "creation-magic-resonance-confirm-progress");
                Require(progress.IsVisible
                    && progress.Text == CreationFlowStrings.Get("Magic.Review.Confirming", "Checking and saving choices…")
                    && button.Text == CreationFlowStrings.Get("Magic.Review.Saving", "Saving…")
                    && progress.TextColor == NativeTheme.Text
                    && !MinimalVisible(page).OfType<ActivityIndicator>().Any(item => item.IsRunning)
                    && rendered.SequenceEqual(body.Children),
                    "Magic confirmation must show readable static progress in place without an animator or review rebuild.");
                await (Task)confirm.Invoke(page, null)!;
                Require(!pending.IsCompleted && probe.Loads == reads && probe.Confirms == 0,
                    "A second pending confirmation must not enter Core or reset the busy state.");
                Require(!button.IsEnabled && progress.IsVisible, "A duplicate confirmation cleared pending feedback.");
            }
            catch (Exception error) { assertion = error; }
            finally { gate.Release(); }
            try
            {
                await pending;
                throw new Exception("SETUP: Magic feedback probe did not propagate its read failure.");
            }
            catch (Exception error) when (error.Message == "magic-feedback-test") { }
            finally { probe.BeforeRead = null; }
            if (assertion is not null) throw assertion;
            var restored = body.Children.OfType<Button>().Single(item => item.AutomationId == button.AutomationId);
            Require(restored.IsEnabled && restored.Text == idleText
                && !body.Children.OfType<Label>().Single(item =>
                    item.AutomationId == "creation-magic-resonance-confirm-progress").IsVisible
                && !body.Children.OfType<ActivityIndicator>().Any(item => item.IsRunning || item.IsVisible),
                "Canceled or failed pre-commit reads must stop progress and restore exact review controls.");
            Require(journal.TryRead(out var unchanged, out _)
                && unchanged.CheckpointDigest == reviewed.CheckpointDigest && probe.Confirms == 0,
                "Pending feedback or a failed read changed the durable Magic command.");
        }
        Console.WriteLine("PASS Magic readable non-animated pending feedback, in-place controls, duplicate exclusion and error/cancel cleanup");
    }

    private sealed class MagicReadProbe(IOwnerBoundCharacterCreationMagicResonanceService inner, ControlledLinkedOwner owners)
        : IOwnerBoundCharacterCreationMagicResonanceService
    {
        public int UiThreadId { get; set; }
        public int Loads { get; private set; }
        public double LastLoadMilliseconds { get; private set; }
        public int Previews { get; private set; }
        public int Confirms { get; private set; }
        public bool AllowConfirm { get; set; }
        public Action? BeforeRead { get; set; }
        private void Check(OwnerContextStamp original)
        {
            Require(Environment.CurrentManagedThreadId != UiThreadId && owners.ActiveLeases == 0
                && original == owners.Capture(),
                "Magic must run off UI with the retained stamp; Core owns its synchronous lease.");
            BeforeRead?.Invoke();
        }
        public CharacterCreationFoundationResult<CharacterCreationMagicResonanceState> Load(
            OwnerContextStamp original, CharacterCreationMagicResonanceLoadRequest request)
        {
            Check(original);
            Loads++;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try { return inner.Load(original, request); }
            finally { LastLoadMilliseconds = watch.Elapsed.TotalMilliseconds; }
        }
        public CharacterCreationFoundationResult<CharacterCreationMagicResonancePreview> Preview(
            OwnerContextStamp original, CharacterCreationMagicResonancePreviewRequest request)
        { Check(original); Previews++; return inner.Preview(original, request); }
        public CharacterCreationFoundationResult<CharacterCreationMagicResonanceReceipt> Confirm(
            OwnerContextStamp original, CharacterCreationMagicResonanceConfirmRequest request)
        {
            Require(AllowConfirm, "Read-only background stage must not confirm.");
            Check(original); Confirms++; return inner.Confirm(original, request);
        }
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
            var presenter = new CharacterCreationGearInteractionPresenter(runtime.Services.GetRequiredService<ICharacterCreationGearService>(),
                runtime.Services.GetRequiredService<IOwnerBoundCharacterCreationGearService>());
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
            await ui.RunAsync(() => VerifyDirectGearRouteAsync(runtime, presenter, owners, ui));
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
        var prepare = typeof(CreationQualitiesPage).GetMethod("PrepareForAppearanceRefreshAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Task Prepare(CreationQualitiesPage target, CancellationToken token = default)
            => (Task)prepare.Invoke(target, [token])!;
        VerticalStackLayout Body() => (VerticalStackLayout)((ScrollView)page.Content!).Content!;
        Border[] Rows() => MinimalVisible(page).OfType<Border>()
            .Where(row => row.Content is Grid grid && grid.Children.OfType<Button>()
                .Any(button => button.AutomationId?.StartsWith("creation-quality-option-", StringComparison.Ordinal) == true)).ToArray();
        string OptionId(Border row) => ((Grid)row.Content!).Children.OfType<Button>()
            .Single(button => button.AutomationId?.StartsWith("creation-quality-option-", StringComparison.Ordinal) == true).AutomationId;
        Button Pager(string suffix) => MinimalVisible(page).OfType<HorizontalStackLayout>()
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
        var state = (CharacterCreationFoundationResult<CharacterCreationQualitiesState>)typeof(CreationQualitiesPage)
            .GetField("_loaded", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
        var available = (IReadOnlyList<CharacterCreationQualitiesDesktopOption>)typeof(CreationQualitiesPage)
            .GetField("_availableOptions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
        Console.WriteLine($"QUALITIES_FILTER available={available.Count} total={state.Value!.Authority.Options.Count}");
        var catalogEditor = CreationQualitiesPhoneAuthority.ProjectEditor(state.Value, runtime.Coordinator.State);
        var catalogDraft = new CreationQualitiesPhoneDraft();
        catalogDraft.Bind(state.Value, runtime.Coordinator.State);
        long allocationStart = GC.GetAllocatedBytesForCurrentThread();
        var catalogTimer = System.Diagnostics.Stopwatch.StartNew();
        string[] expectedIds = catalogEditor.Options
            .Where(option => CreationQualitiesPhoneAuthority.IsOptionConfigurable(option)
                && CharacterCreationQualitiesRules.Evaluate(new(state.Value.Binding, state.Value.Authority,
                    [option.OptionId])).Blockers.All(blocker => blocker == CharacterCreationQualitiesBlockers.MetagenicImbalanced))
            .Select(option => option.OptionId).ToArray();
        double individualMilliseconds = catalogTimer.Elapsed.TotalMilliseconds;
        long individualBytes = GC.GetAllocatedBytesForCurrentThread() - allocationStart;
        allocationStart = GC.GetAllocatedBytesForCurrentThread();
        catalogTimer.Restart();
        var repeatedBatch = catalogDraft.AvailableOptions(state.Value, runtime.Coordinator.State,
            catalogEditor, CancellationToken.None);
        double batchMilliseconds = catalogTimer.Elapsed.TotalMilliseconds;
        long batchBytes = GC.GetAllocatedBytesForCurrentThread() - allocationStart;
        Require(available.Select(option => option.OptionId).SequenceEqual(expectedIds)
            && repeatedBatch.Select(option => option.OptionId).SequenceEqual(expectedIds),
            "Batch filtering must exactly preserve every eligible option and its order, not just exclude illegal rows.");
        Console.WriteLine($"QUALITIES_BATCH managed-real-catalog total={state.Value.Authority.Options.Count} "
            + $"individual={individualMilliseconds:F2}ms/{individualBytes}B batch={batchMilliseconds:F2}ms/{batchBytes}B");
        Require(available.Count > 0 && available.Count < state.Value.Authority.Options.Count
            && available.All(option => option.IsSelectable && CharacterCreationQualitiesRules.Evaluate(new(
                state.Value.Binding, state.Value.Authority, [option.OptionId])).Blockers.All(blocker =>
                    blocker == CharacterCreationQualitiesBlockers.MetagenicImbalanced)),
            "Available-only catalog exposed an unavailable, over-budget or duplicate quality.");
        var navigation = new NavigationPage(page);
        Button help = ((Grid)Rows()[0].Content!).Children.OfType<Button>().Single(button => button.Text == "!");
        Require(help.WidthRequest >= 48 && help.HeightRequest >= 48
            && Microsoft.Maui.Controls.SemanticProperties.GetDescription(help).StartsWith("Explain "),
            "Quality information needs a separate accessible touch target.");
        ((IButtonController)help).SendClicked();
        Require(navigation.CurrentPage is CreationQualityInfoPage,
            "The quality ! button did not open read-only information.");
        MinimalRequireNoMachineValues(navigation.CurrentPage);
        await navigation.PopAsync(false);
        RequireSameRewardDocument(before, new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!);
        var qualityState = state.Value ?? throw new InvalidOperationException("SETUP: missing quality state.");
        var analytical = qualityState.Authority.Options.First(option => option.Name == "Analytical Mind");
        var explanation = string.Join(" ", CreationQualityInfo.Effects(analytical.SourceNodeXml));
        Require(explanation.Contains("Bonus: 2") && explanation.Contains("pattern recognition", StringComparison.OrdinalIgnoreCase)
            && explanation.Contains("not every Logic test") && !explanation.Contains("Rulebook"),
            "Quality help lost the inline explanation, source modifier or condition.");
        var mystic = qualityState.Authority.Options.First(option => option.Name == "Mystic Adept");
        var mysticEffects = string.Join(" ", CreationQualityInfo.Effects(mystic.SourceNodeXml));
        Require(mysticEffects.Contains("Magic") && mysticEffects.Contains("magician")
            && mysticEffects.Contains("adept") && mysticEffects.Contains("Unlocks skills"),
            "Mystic Adept help omitted its actual source-backed attribute, capabilities or skill access.");
        var grant = new CharacterCreationGrantedQuality("read-only-help-test", analytical.SourceId,
            analytical.SelectionKey, analytical.Name, analytical.Type, analytical.Rating, analytical.KarmaCost,
            analytical.IsMetagenic, false, false, "Heritage", analytical.SourceAnchorIds, "read-only-fixture");
        Require(CreationQualityInfo.SourceForGrant(grant, qualityState.Authority.Options) == analytical.SourceNodeXml
            && CreationQualityInfo.SourceForGrant(grant with { SourceId = Guid.NewGuid() }, qualityState.Authority.Options) is null
            && CreationQualityInfo.SourceForGrant(grant,
                [analytical, analytical with { SourceNodeXml = analytical.SourceNodeXml + " " }]) is null,
            "Granted help must use one exact source identity, never names or conflicting source bytes.");
        // Read-only rendering fixture; no synthetic grant enters Core or storage.
        typeof(CreationQualitiesPage).GetMethod("AddGranted", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(page, [qualityState with { Authority = qualityState.Authority with { GrantedQualities = [grant] } }]);
        MinimalRequireNoMachineValues(page);
        Button grantHelp = MinimalVisible(page).OfType<Button>().Single(button =>
            button.AutomationId == "creation-quality-granted-info-read-only-help-test");
        await ui.BeginAsyncVoid(() => ((IButtonController)grantHelp).SendClicked());
        string grantSummary = CreationFlowStrings.Get("Qualities.Summary." + analytical.SourceId.ToString("D"), string.Empty);
        Require(navigation.CurrentPage is CreationQualityInfoPage
            && !string.IsNullOrWhiteSpace(grantSummary)
            && MinimalVisibleText(navigation.CurrentPage).Contains(grantSummary)
            && !MinimalVisibleText(navigation.CurrentPage).Contains("Bonus: 2"),
            "Granted quality must show its concise explanation before the folded supporting values. Actual: "
                + navigation.CurrentPage.GetType().Name + " / " + MinimalVisibleText(navigation.CurrentPage));
        Button grantEffects = MinimalVisible(navigation.CurrentPage).OfType<Button>().Single(button =>
            button.AutomationId == "creation-quality-info-effects-toggle");
        ((IButtonController)grantEffects).SendClicked();
        Require(MinimalVisibleText(navigation.CurrentPage).Contains("Bonus: 2"),
            "Granted quality must expose its source-backed bonus when effect details are expanded.");
        ((IButtonController)grantEffects).SendClicked();
        Require(!MinimalVisibleText(navigation.CurrentPage).Contains("Bonus: 2")
            && MinimalVisibleText(navigation.CurrentPage).Contains(grantSummary),
            "Folding granted-quality details must retain the concise explanation.");
        MinimalRequireNoMachineValues(navigation.CurrentPage);
        await navigation.PopAsync(false);
        RequireSameRewardDocument(before, new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!);
        var editor = CreationQualitiesPhoneAuthority.ProjectEditor(qualityState, runtime.Coordinator.State);
        var draft = new CreationQualitiesPhoneDraft();
        draft.Bind(qualityState, runtime.Coordinator.State);
        var expensive = available.Where(option => option.Type == CharacterCreationQualityType.Positive)
            .OrderByDescending(option => option.KarmaCost).First();
        var chosen = new[] { expensive.OptionId };
        var quoted = CharacterCreationQualitiesRules.Evaluate(new(qualityState.Binding, qualityState.Authority, chosen));
        Require(draft.TryAdopt(qualityState, runtime.Coordinator.State,
            new(CharacterCreationFoundationOutcomes.Success, quoted, quoted.Blockers), chosen), "SETUP: quality draft unavailable.");
        var afterSelection = draft.AvailableOptions(qualityState, runtime.Coordinator.State, editor, CancellationToken.None);
        var expectedAfterSelection = editor.Options.Where(option => option.OptionId == expensive.OptionId
            || CreationQualitiesPhoneAuthority.IsOptionConfigurable(option)
                && CharacterCreationQualitiesRules.Evaluate(new(qualityState.Binding, qualityState.Authority,
                    [expensive.OptionId, option.OptionId])).Blockers.All(blocker =>
                        blocker == CharacterCreationQualitiesBlockers.MetagenicImbalanced))
            .Select(option => option.OptionId);
        Require(afterSelection.Select(option => option.OptionId).SequenceEqual(expectedAfterSelection)
            && afterSelection.Any(option => option.OptionId == expensive.OptionId),
            "Budget filtering must preserve every eligible addition in order and retain the remove action.");
        Require(draft.WithToggle(expensive).Count == 0, "Filtering changed removal semantics.");
        // Same exact source catalog, but a deterministic zero-Karma fixture.
        // This is a read-only quote, not a modification of the stored runner.
        var spentBinding = qualityState.Binding with { CreationKarmaUsedBeforeQualities = qualityState.Binding.CreationKarmaTotal };
        var spentState = qualityState with { Binding = spentBinding,
            Preview = CharacterCreationQualitiesRules.Evaluate(new(spentBinding, qualityState.Authority, [])) };
        spentState = spentState with { SnapshotDigest = CharacterCreationQualitiesRules.ComputeStateDigest(spentState) };
        var spentDraft = new CreationQualitiesPhoneDraft();
        spentDraft.Bind(spentState, runtime.Coordinator.State);
        var affordable = spentDraft.AvailableOptions(spentState, runtime.Coordinator.State,
            CreationQualitiesPhoneAuthority.ProjectEditor(spentState, runtime.Coordinator.State), CancellationToken.None);
        Require(affordable.Count > 0 && affordable.Count < available.Count
            && !affordable.Any(option => option.OptionId == expensive.OptionId),
            "Zero-Karma catalog still offers a paid positive quality.");
        using (var stopped = new CancellationTokenSource())
        {
            stopped.Cancel();
            try { draft.AvailableOptions(qualityState, runtime.Coordinator.State, editor, stopped.Token);
                throw new InvalidOperationException("Canceled availability preparation continued."); }
            catch (OperationCanceledException) when (stopped.IsCancellationRequested) { }
        }
        string[] first = Rows().Select(OptionId).ToArray();
        Require(first.Length == Math.Min(20, available.Count) && Pager("next").IsEnabled == (available.Count > 20)
            && !Pager("previous").IsEnabled,
            "The real catalog must render only the first bounded page, with honest navigation.");
        Button review = Body().Children.OfType<Button>().Single(button => button.AutomationId == "creation-qualities-open-review");
        Require(review.IsEnabled && Rows()[0].Parent is IView catalog
            && Body().Children.IndexOf(review) < Body().Children.IndexOf(catalog),
            "Review must be available before the catalog, including an empty valid selection.");

        if (Pager("next").IsEnabled)
        {
            ((IButtonController)Pager("next")).SendClicked();
            Require(Rows().Length == Math.Min(20, available.Count - 20) && !Rows().Select(OptionId).Intersect(first).Any()
                    && Pager("previous").IsEnabled,
                "Next must show another bounded set of exact Core identities.");
            ((IButtonController)Pager("previous")).SendClicked();
            Require(Rows().Select(OptionId).SequenceEqual(first),
                "Previous must restore the same exact identities.");
        }

        var search = Body().Children.OfType<SearchBar>().Single();
        search.Text = "no-such-quality-regression-20260919";
        ((ISearchBarController)search).OnSearchButtonPressed();
        Require(Rows().Length == 0 && !Pager("next").IsEnabled && !Pager("previous").IsEnabled,
            "An empty search must not leave stale rows or enabled navigation.");
        search.Text = string.Empty;
        Require(ReferenceEquals(search, Body().Children.OfType<SearchBar>().Single())
            && Rows().Select(OptionId).SequenceEqual(first),
            "Clearing search must return to the first catalog page.");
        Require(probe.LoadCalls == 1,
            "Rendering, paging and filtering must not reload Core after appearance preparation.");

        await VerifyQualitiesConfirmationFeedbackAsync(runtime.Coordinator, probe, qualityState);

        var draftField = typeof(CreationQualitiesPage).GetField("_draft", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var pageDraft = (CreationQualitiesPhoneDraft)draftField.GetValue(page)!;
        Require(pageDraft.TryAdopt(qualityState, runtime.Coordinator.State,
            new(CharacterCreationFoundationOutcomes.Success, quoted, quoted.Blockers), chosen),
            "SETUP: page must retain an unsaved exact quality selection.");
        probe.ReturnUnavailable = true;
        await Prepare(page);
        refresh.Invoke(page, null);
        Require(Rows().Length == 0 && !MinimalVisible(page).OfType<Button>().Any()
            && ((CreationQualitiesPhoneDraft)draftField.GetValue(page)!).SelectedOptionIds.SequenceEqual(chosen),
            "A temporarily unavailable catalog must hide all actions without discarding unsaved selection.");
        probe.ReturnUnavailable = false;
        await Prepare(page);
        refresh.Invoke(page, null);
        Require(((CreationQualitiesPhoneDraft)draftField.GetValue(page)!).SelectedOptionIds.SequenceEqual(chosen)
            && Rows().Length > 0,
            "Fresh exact validation after an unavailable read did not restore the unsaved choice.");
        int loadsBeforeCancellation = probe.LoadCalls;

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
        Require(probe.LoadCalls == loadsBeforeCancellation + 1, "Queued Qualities load crossed an owner A→B→A transition.");
        refresh.Invoke(page, null);
        Require(Rows().Length == 0 && !Body().Children.OfType<Button>()
                .Any(button => button.AutomationId == "creation-qualities-open-review"),
            "An old cached catalog remained actionable after an owner transition.");

        RequireSameRewardDocument(before, new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!);
        Require(runtime.Coordinator.State.ContentRevision == before.ContentRevision,
            "Catalog navigation must not mutate the workspace or increment its revision.");
        Console.WriteLine("PASS native Qualities background read, live UI heartbeat, one-read paging/search, canceled load and owner ABA rejection");
    }

    private static async Task VerifyDirectGearRouteAsync(NativeRewardRuntime runtime,
        ICharacterCreationGearInteractionPresenter presenter, ControlledLinkedOwner owners, IssuedPageUiContext ui)
    {
        var before = new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!;
        var resourcesPresenter = new CharacterCreationResourcesInteractionPresenter(
            runtime.Services.GetRequiredService<ICharacterCreationResourcesService>(),
            runtime.Services.GetRequiredService<IOwnerBoundCharacterCreationResourcesService>());
        var resources = resourcesPresenter.Load(runtime.Coordinator.State);
        Require(resources.State?.PendingDraft is not null
            && CreationResourcesPhoneAuthority.IsReady(resources.State, runtime.Coordinator.State),
            "SETUP: Gear navigation needs the current saved Resources draft.");
        var render = typeof(BuildPage).GetMethod("AddWizardStages", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var resolve = typeof(BuildPage).GetMethod("CreationGearRoute", BindingFlags.Instance | BindingFlags.NonPublic)!;
        foreach (bool ready in new[] { false, true })
        foreach (bool wired in new[] { false, true })
        {
            var page = new BuildPage(runtime.Coordinator, resourcesPresenter, runtime.Presenter, wired ? presenter : null);
            var navigation = new NavigationPage(page);
            _ = new Window(navigation);
            var readiness = new CreationDashboardRenderReadiness(
                () => false, () => false, () => false, () => false, () => false, () => ready);
            render.Invoke(page, [runtime.Coordinator.State.CreationWizard!, null, null, null, null,
                null, resources, readiness]);
            var button = MinimalVisible(page).OfType<Button>().Single(item => item.AutomationId == "creation-stage-gear");
            Require(button.IsEnabled == (ready && wired),
                "Direct Gear entry borrowed readiness or lost its required presenter.");
            if (button.IsEnabled)
            {
                await ui.BeginAsyncVoid(() => ((IButtonController)button).SendClicked());
                Require(navigation.Navigation.NavigationStack.Last() is CreationGearPage,
                    "The Gear row did not open the actual Gear editor directly.");
                await navigation.PopAsync(false);
            }
            else
            {
                var route = (CreationBudgetRoute)resolve.Invoke(page, [resources, ready])!;
                await route.Open();
                Require(navigation.Navigation.NavigationStack.Count == 1,
                    "A disabled Gear route still navigated.");
            }
        }
        var guarded = new BuildPage(runtime.Coordinator, resourcesPresenter, runtime.Presenter, presenter);
        var nav = new NavigationPage(guarded);
        _ = new Window(nav);
        foreach (var missing in new CharacterCreationResourcesInteractionLoadResult?[]
        {
            null,
            resources with { State = resources.State! with { PendingDraft = null } },
            resources with { State = resources.State! with
                { Binding = resources.State!.Binding with { ContentRevision = resources.State.Binding.ContentRevision + 1 } } }
        })
        {
            var route = (CreationBudgetRoute)resolve.Invoke(guarded, [missing, true])!;
            Require(!route.CanOpen, "Gear admitted absent, unsaved or stale Resources.");
            await route.Open();
        }
        var oldRender = (CreationBudgetRoute)resolve.Invoke(guarded, [resources, true])!;
        var generation = typeof(BuildPage).GetField("_dossierRenderGeneration", BindingFlags.NonPublic | BindingFlags.Instance)!;
        generation.SetValue(guarded, (long)generation.GetValue(guarded)! + 1);
        await oldRender.Open();
        var oldOwner = (CreationBudgetRoute)resolve.Invoke(guarded, [resources, true])!;
        var originalOwner = owners.Capture().Owner;
        owners.Set(ContactsOwnerB);
        owners.Set(originalOwner);
        await oldOwner.Open();
        Require(nav.Navigation.NavigationStack.Count == 1,
            "A stale render or A→B→A owner transition revived the Gear route.");
        RequireSameRewardDocument(before, new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!);
        await HydrateFinalizationOwnerAsync(runtime, owners, before);
        ui.AssertHealthy();
        Console.WriteLine("PASS direct Gear row: typed saved Resources, real editor, missing/stale/owner/render rejection, unchanged saved bytes");
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
            Require(Environment.CurrentManagedThreadId != UiThreadId && owners.ActiveLeases == 0,
                "Gear read must run off UI without a host lease nested around the Core-owned lease.");
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

    private static async Task VerifyQualitiesConfirmationFeedbackAsync(RunnerSessionCoordinator coordinator,
        QualitiesReadProbe probe, CharacterCreationQualitiesState state)
    {
        var original = coordinator.State;
        var preview = await Task.Run(() => coordinator.ReadCreationAuthority(original,
            () => coordinator.PreviewCreationQualities(state.Binding, [], original), CancellationToken.None));
        Require(preview.Value is { CanConfirm: true }, "SETUP: empty Qualities review must be valid.");
        var journal = new CharacterCreationQualitiesCheckpointStore(new QualitiesFeedbackBackend());
        var reviewed = CharacterCreationQualitiesCheckpoint.CreateReviewed(preview.Value!, [], Guid.NewGuid());
        Require(journal.TryCreate(reviewed, out reviewed, out var blocker), blocker);
        var page = new CreationQualitiesReviewPage(coordinator, reviewed, journal, original);
        var refresh = typeof(CreationQualitiesReviewPage).GetMethod("Refresh", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var apply = typeof(CreationQualitiesReviewPage).GetMethod("ApplyAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var gate = (SemaphoreSlim)typeof(RunnerSessionCoordinator)
            .GetField("_workspaceActivationGate", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(coordinator)!;
        foreach (bool cancel in new[] { false, true })
        {
            refresh.Invoke(page, null);
            var body = (VerticalStackLayout)((ScrollView)page.Content!).Content!;
            var button = body.Children.OfType<Button>().Single(item => item.AutomationId == "creation-qualities-confirm-draft");
            var progress = body.Children.OfType<Label>().Single(item => item.AutomationId == "creation-qualities-confirm-progress");
            string idleText = button.Text;
            var rendered = body.Children.ToArray();
            Require(button.IsEnabled && !progress.IsVisible, "SETUP: exact Qualities review must allow saving.");
            int reads = probe.LoadCalls;
            probe.BeforeLoad = () =>
            {
                if (cancel) throw new OperationCanceledException("qualities-feedback-test");
                throw new InvalidOperationException("qualities-feedback-test");
            };
            await gate.WaitAsync();
            Task pending = (Task)apply.Invoke(page, null)!;
            Exception? assertion = null;
            try
            {
                Require(!pending.IsCompleted && !button.IsEnabled
                    && button.Text == CreationFlowStrings.Get("Qualities.Review.Saving", "Saving…")
                    && progress.IsVisible
                    && progress.Text == CreationFlowStrings.Get("Qualities.Review.Confirming", "Checking and saving qualities…")
                    && progress.TextColor == NativeTheme.Text && rendered.SequenceEqual(body.Children)
                    && !MinimalVisible(page).OfType<ActivityIndicator>().Any(item => item.IsRunning),
                    "Qualities must disable and relabel the actual button, with readable in-place progress before awaiting Core.");
                await (Task)apply.Invoke(page, null)!;
                Require(!pending.IsCompleted && probe.LoadCalls == reads && probe.Confirms == 0
                    && !button.IsEnabled && progress.IsVisible,
                    "A second pending Qualities save entered Core or reset busy feedback.");
            }
            catch (Exception error) { assertion = error; }
            finally { gate.Release(); }
            try
            {
                await pending;
                throw new Exception("SETUP: Qualities feedback probe did not propagate its read failure.");
            }
            catch (Exception error) when (error.Message == "qualities-feedback-test") { }
            finally { probe.BeforeLoad = null; }
            if (assertion is not null) throw assertion;
            Require(button.IsEnabled && button.Text == idleText && !progress.IsVisible
                && journal.TryRead(out var unchanged, out _)
                && unchanged.CheckpointDigest == reviewed.CheckpointDigest && probe.Confirms == 0,
                "Canceled or failed Qualities reads must restore review controls without changing the command or character.");
        }
        Console.WriteLine("PASS Qualities immediate readable save feedback, duplicate exclusion, unchanged journal and error/cancel cleanup");
    }

    private sealed class QualitiesFeedbackBackend : ICharacterCreationQualitiesCheckpointBackend
    {
        private string _payload = string.Empty;
        public string Read() => _payload;
        public void Write(string payload) => _payload = payload;
        public void Remove() => _payload = string.Empty;
    }

    private sealed class QualitiesReadProbe(IOwnerBoundCharacterCreationQualitiesService inner,
        ControlledLinkedOwner owners) : IOwnerBoundCharacterCreationQualitiesService
    {
        public int UiThreadId { get; set; }
        public int LoadCalls { get; private set; }
        public int Confirms { get; private set; }
        public Action? BeforeLoad { get; set; }
        public bool ReturnUnavailable { get; set; }
        public CharacterCreationFoundationResult<CharacterCreationQualitiesState> Load(OwnerContextStamp owner, CharacterCreationQualitiesLoadRequest request)
        {
            Require(Environment.CurrentManagedThreadId != UiThreadId && owners.ActiveLeases == 0
                    && owners.Capture() == owner,
                "Qualities Core read must run off UI with its exact owner; Core owns the synchronous lease.");
            LoadCalls++;
            BeforeLoad?.Invoke();
            var result = inner.Load(owner, request);
            return ReturnUnavailable && result.Value is { } state
                ? new(CharacterCreationFoundationOutcomes.Blocked, state with { CanEdit = false },
                    [CharacterCreationQualitiesBlockers.AuthorityUnavailable])
                : result;
        }
        public CharacterCreationFoundationResult<CharacterCreationQualitiesPreview> Preview(OwnerContextStamp owner, CharacterCreationQualitiesPreviewRequest request)
            => inner.Preview(owner, request);
        public CharacterCreationFoundationResult<CharacterCreationQualitiesDraftReceipt> Confirm(OwnerContextStamp owner, CharacterCreationQualitiesConfirmRequest request)
        { Confirms++; return inner.Confirm(owner, request); }
    }

    // Test-only export of the existing actual-Core fixture for a bounded native
    // cash-entry smoke. Never fabricates rules, admission or a finalization receipt.
    public static async Task ExportStartingCashSeedAsync(string contentRoot, string directory,
        bool contactsPending = false, bool resourcesPending = false)
    {
        Require(Path.IsPathFullyQualified(directory) && Directory.Exists(directory)
            && !Directory.EnumerateFileSystemEntries(directory).Any(),
            "Seed destination must be explicit and empty.");
        await using var runtime = new NativeRewardRuntime(contentRoot, creationFinalization: true);
        var saved = PrepareActualFinalizationReadyContext(runtime,
            fixtureAlias: resourcesPending ? "Contacts-Resources-Smoke"
                : contactsPending ? "Contacts-Readiness-Smoke" : "Cash-Rejection-Smoke",
            stopBeforeGear: contactsPending, stopBeforeResources: resourcesPending);
        string sourceDirectory = Path.Combine(runtime.StateDirectory, "workspaces");
        Require(Directory.Exists(sourceDirectory), "The actual fixture did not persist its workspaces.");
        foreach (string source in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(directory, "workspaces", Path.GetRelativePath(sourceDirectory, source));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, overwrite: false);
        }
        Console.WriteLine($"SEED {saved.Id.Value} revision={saved.ContentRevision}/{saved.SavedRevision}");
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
                Require(IssuedElements(page).OfType<Label>().Any(label => label.IsVisible
                    && label.Text == CreationAllocationStrings.Get("Finalization.InvalidDiceTotal",
                        "That total does not match these dice. Check your roll and try again.")),
                    "Core rejected the dice total, but the cash page did not show readable feedback.");
                Require(!IssuedElements(page).OfType<Label>().Any(label => label.IsVisible
                    && (label.Text == CharacterCreationFinalizationBlockers.StartingCashChoiceRequired
                        || label.Text == CharacterCreationFinalizationBlockers.StartingCashChoiceInvalid)),
                    "Starting-cash rejection exposed an internal blocker code.");
                Require(IssuedElements(page).OfType<Label>().Count(label => label.IsVisible
                    && label.Text == CreationKarmaCopy.DiceTotal) == 1,
                    "The invalid total produced a duplicate required-choice prompt.");
                var rejectedInput = input;
                input = Element<Entry>("creation-starting-cash-roll");
                Require(input.Text == int.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "Showing the rejection discarded the entered dice total.");
                rejectedInput.Text = "1";
                Require(input.Text == int.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "A detached pre-rejection Entry changed the current input.");
                RequireSameRewardDocument(before, new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!);
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
                var binding = Element<Label>("creation-finalization-binding");
                Require(!binding.IsVisible, "Review hashes are visible by default.");
                ClickDetails("creation-finalization-technical-details-toggle");
                Require(binding.IsVisible, "The current review cannot disclose its exact binding.");
                ClickDetails("creation-finalization-technical-details-toggle");
                Require(!binding.IsVisible, "The review binding could not collapse.");
                await Click("creation-finalization-confirm");
                Require(Current() is CreationFinalizationReceiptPage, "The actual confirm did not show its receipt.");
                var technical = Element<VerticalStackLayout>("creation-finalization-receipt-technical-details");
                Require(!technical.IsVisible && Element<Button>("creation-finalization-open-career").IsEnabled,
                    "Receipt diagnostics are exposed by default or Career action was lost.");
                ClickDetails("creation-finalization-receipt-technical-details-toggle");
                Require(technical.IsVisible, "Receipt diagnostics could not open.");
                ClickDetails("creation-finalization-receipt-technical-details-toggle");
                Require(!technical.IsVisible, "Receipt diagnostics could not collapse.");
                var detachedToggle = Element<Button>("creation-finalization-receipt-technical-details-toggle");
                typeof(CreationFinalizationReceiptPage).GetMethod("Refresh", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(Current(), null);
                ((IButtonController)detachedToggle).SendClicked();
                Require(!technical.IsVisible && !Element<VerticalStackLayout>("creation-finalization-receipt-technical-details").IsVisible,
                    "A detached receipt control disclosed stale data.");
                var cold = new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!;
                var receipt = cold.Document.AuxiliaryState.CharacterCreationFinalizationReceipts!.Single().Receipt;
                Require(cold.ContentRevision == before.ContentRevision + 1 && cold.SavedRevision == cold.ContentRevision
                    && receipt.StartingCash == review.Plan.StartingCash
                    && receipt.StartingCashAuthorityDigest == review.Plan.StartingCashAuthorityDigest,
                    "The native confirm lost its cash choice or saved more than one revision.");
                owners.Set(ContactsOwnerB);
                owners.Set(OwnerScope.LocalSingleUser);
                Require(!runtime.Coordinator.IsCreationFinalizationStateCurrent(loaded), "Owner ABA revived the old cash page.");
                ClickDetails("creation-finalization-receipt-technical-details-toggle");
                Require(!Element<VerticalStackLayout>("creation-finalization-receipt-technical-details").IsVisible,
                    "Owner ABA revived disclosure of an old receipt.");
                ui.AssertHealthy();
                Console.WriteLine("PASS " + method + " starting-cash page: explicit roll, Core rejection, preview, stale Entry, confirm and cold receipt");

                NativePageBase Current() => (NativePageBase)navigation.Navigation.NavigationStack.Last();
                T Element<T>(string id) where T : Element => IssuedElements(Current()).OfType<T>().Single(e => e.AutomationId == id);
                void ClickDetails(string id)
                {
                    var button = Element<Button>(id);
                    Require(button.IsEnabled, "Details button disabled: " + id);
                    ((IButtonController)button).SendClicked();
                }
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

    public static async Task RunSumToTenFinalizationAsync(string contentRoot)
    {
        if (!Path.IsPathFullyQualified(contentRoot) || !Directory.Exists(Path.Combine(contentRoot, "data")))
            throw new ArgumentException("Supply the explicit canonical Core content root.", nameof(contentRoot));
        await VerifyCreationReadAdmissionAsync();
        foreach (string method in new[] { CharacterCreationBuildMethods.Priority, CharacterCreationBuildMethods.SumToTen })
        {
            await RunPriorityTableAttributesEntryAsync(contentRoot, method);
            await RunPriorityTableAttributesEntryAsync(contentRoot, method, linked: true);
        }
        await RunCreationFinalizationLocalBaselineAsync(contentRoot, CharacterCreationBuildMethods.SumToTen);
    }

    public static async Task RunCreationAttributesAsync(string contentRoot)
    {
        if (!Path.IsPathFullyQualified(contentRoot) || !Directory.Exists(Path.Combine(contentRoot, "data")))
            throw new ArgumentException("Supply the explicit canonical Core content root.", nameof(contentRoot));
        await VerifyCreationReadAdmissionAsync();
        foreach (string method in new[] { CharacterCreationBuildMethods.Priority, CharacterCreationBuildMethods.SumToTen })
        {
            await RunPriorityTableAttributesEntryAsync(contentRoot, method, attributesOnly: true);
            await RunPriorityTableAttributesEntryAsync(contentRoot, method, linked: true, attributesOnly: true);
        }
    }

    private static async Task VerifyCreationReadAdmissionAsync()
    {
        // The production Android authority uses non-blocking lease admission,
        // unlike ControlledLinkedOwner. Exercise that exact credential gate;
        // transport/key/storage dependencies are never invoked by this test.
        var account = new Chummer.Android.Platform.AndroidAccountLinkService(null!, null!, null!, null!);
        var owners = new AndroidAccountOwnerContextAccessor(account);
        var gate = (SemaphoreSlim)typeof(Chummer.Android.Platform.AndroidAccountLinkService)
            .GetField("_credentialCommitGate", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(account)!;
        gate.Wait();
        try { account.OwnerAuthority.PublishLocal(); }
        finally { gate.Release(); }
        var original = owners.Capture();
        foreach (string scenario in new[] { "contention", "cancel", "aba" })
        {
            using var ct = new CancellationTokenSource();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            gate.Wait();
            Task<bool> read = Task.Run(() => owners.RunScheduledRead(original, () =>
            {
                entered.SetResult();
                bool admitted = owners.TryAcquire(original, out var lease);
                using (lease)
                {
                    if (admitted)
                        Require(lease!.Stamp == original && !owners.TryAcquire(original, out _),
                            "The read changed owner or admitted a nested lease.");
                    return admitted;
                }
            }, ct.Token));
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Require(await Task.WhenAny(read, Task.Delay(80)) != read,
                    "A concurrent dashboard read was reported unavailable instead of waiting for its exact owner.");
                if (scenario == "cancel") ct.Cancel();
                if (scenario == "aba")
                {
                    account.OwnerAuthority.PublishLinked("fixture-install", "fixture-grant", "fixture-owner", DateTimeOffset.UtcNow.AddHours(1));
                    account.OwnerAuthority.PublishLocal();
                }
            }
            finally { gate.Release(); }
            try
            {
                bool admitted = await read.WaitAsync(TimeSpan.FromSeconds(5));
                Require(scenario != "cancel" && admitted == (scenario == "contention"),
                    "Cancellation or an owner transition granted stale read authority: " + scenario);
            }
            catch (OperationCanceledException) when (scenario == "cancel" && ct.IsCancellationRequested) { }
        }
        // A scope must not leak its blocking behavior to ordinary admission.
        gate.Wait();
        try { Require(!owners.TryAcquire(owners.Capture(), out _), "Read admission leaked outside the scheduled read."); }
        finally { gate.Release(); }
        Console.WriteLine("PASS actual Android Creation read admission: contention, cancellation, owner ABA, no nested or leaked lease");
    }

    private static async Task RunPriorityTableAttributesEntryAsync(string contentRoot, string method,
        bool linked = false, bool attributesOnly = false)
    {
        var owners = new ControlledLinkedOwner();
        AttributesCommitProbe? probe = null;
        await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners,
            creationFinalization: true, creationAttributes: true, creationSkills: true, productionCreationOverview: true,
            attributesDecorator: actual => probe = new(actual));
        var before = PrepareActualFinalizationReadyContext(runtime, buildMethod: method, stopBeforeAttributes: true);
        if (linked)
        {
            CloneFinalizationRecordFixture(runtime, ContactsOwnerA, before);
            Require(new FileWorkspaceStore(runtime.StateDirectory).Delete(before.Id, before.ContentRevision).Success,
                "SETUP: could not remove the synthetic local copy masking an unscoped read.");
            owners.Set(ContactsOwnerA);
        }
        await HydrateFinalizationOwnerAsync(runtime, owners, before);
        var result = runtime.Coordinator.LoadCreationAttributes();
        Require(result is { Outcome: CharacterCreationFoundationOutcomes.Success, Value: { } state }
            && CreationAttributesPhoneAuthority.IsReady(state, runtime.Coordinator.State),
            "Confirmed prerequisite did not admit actual phone Attributes: " + JsonSerializer.Serialize(new
            { method, linked, result, runtime.Coordinator.State.ContentRevision, runtime.Coordinator.State.SavedRevision }));
        var stage = runtime.Coordinator.State.CreationWizard!.Steps.Single(item =>
            item.StepId == CharacterCreationWizardStepIds.Attributes);
        Require(BuildPageUiProjection.CanOpenExactTypedCreationStage(stage,
            CharacterCreationWizardStepIds.Attributes, exactTypedAuthorityReady: true),
            "Production overview blocks the ready Attributes editor: " + JsonSerializer.Serialize(new { method, stage }));
        var coldStore = new FileWorkspaceStore(runtime.StateDirectory);
        var cold = linked ? coldStore.Get(owners.Current, runtime.Id) : coldStore.Get(runtime.Id);
        Require(FinalizationDocumentDigest(cold.Value!)
            == FinalizationDocumentDigest(before), "Read-only Attributes admission changed the saved draft.");
        var coordinator = runtime.Coordinator;
        var loaded = result.Value!;
        CharacterCreationAttributeAllocation[] allocations = loaded.Attributes
            .Select(item => new CharacterCreationAttributeAllocation(item.AttributeId, item.AttributeId == "BOD" ? 1 : 0, 0))
            .ToArray();
        var preview = coordinator.PreviewCreationAttributes(loaded.Binding, allocations).Value!;
        Require(preview is { CanConfirm: true } && coordinator.IsCreationAttributesPreviewCurrent(preview),
            "Real allocation preview was not admitted.");
        Require((await coordinator.ConfirmCreationAttributesAsync(preview with { }, allocations)).Receipt is null,
            "A copied, unissued preview became actionable.");
        Require((await coordinator.ConfirmCreationAttributesAsync(preview, [new("BOD", 2, 0)])).Receipt is null,
            "Confirmation accepted allocations different from the reviewed values.");
        OwnerScope originalOwner = owners.Current;
        WorkspaceStoredDocument ReadSaved()
        {
            var store = new FileWorkspaceStore(runtime.StateDirectory);
            return (originalOwner.IsLocalSingleUser ? store.Get(runtime.Id) : store.Get(originalOwner, runtime.Id)).Value!;
        }
        owners.Set(ContactsOwnerB);
        owners.Set(originalOwner);
        Require(!coordinator.IsCreationAttributesStateCurrent(loaded)
            && !coordinator.CanDisplayCreationAttributesPreview(preview)
            && coordinator.RevalidateCreationAttributes(loaded).Value is null
            && (await coordinator.ConfirmCreationAttributesAsync(preview, allocations)).Receipt is null,
            "Owner A→B→A revived a retained Attributes page or preview.");
        Require(FinalizationDocumentDigest(ReadSaved())
            == FinalizationDocumentDigest(before), "Rejected allocation attempts changed the workspace.");
        await HydrateFinalizationOwnerAsync(runtime, owners, before);
        loaded = coordinator.LoadCreationAttributes().Value!;
        preview = coordinator.PreviewCreationAttributes(loaded.Binding, allocations).Value!;
        var applied = await coordinator.ConfirmCreationAttributesAsync(preview, allocations);
        Require(applied.Outcome == CharacterCreationFoundationOutcomes.Success && applied.Receipt is not null
            && applied.Blockers.Count == 0 && coordinator.IsCreationAttributesReceiptCurrent(applied.Receipt),
            "Real owner-bound Attributes confirmation failed: " + JsonSerializer.Serialize(applied));
        var saved = ReadSaved();
        Require(saved.ContentRevision == before.ContentRevision + 1 && saved.ContentRevision == saved.SavedRevision
            && saved.Document.Content == before.Document.Content,
            "Attributes must save one pending draft, not mutate live XML or duplicate revisions.");
        Require((await coordinator.ConfirmCreationAttributesAsync(preview, allocations)).Receipt is null,
            "The old confirmation was replayed after a successful save.");
        await HydrateFinalizationOwnerAsync(runtime, owners, saved);
        var reopened = coordinator.LoadCreationAttributes().Value!;
        Require(reopened.PendingDraft is not null && reopened.Attributes.Single(item => item.AttributeId == "BOD").Current == 2
            && coordinator.IsCreationAttributesStateCurrent(reopened) && owners.ActiveLeases == 0,
            "Saved allocation did not reopen under fresh owner authority.");
        VerifyAttributesLocalUndo(runtime, reopened, ReadSaved);
        await VerifyAttributesBackgroundPreviewAsync(runtime, owners, probe!, reopened, ReadSaved);
        reopened = coordinator.LoadCreationAttributes().Value!;
        if (linked) Require(!new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Success,
            "Owner-bound allocation created a hidden legacy-local record.");
        using var canceled = new CancellationTokenSource();
        probe!.AfterConfirm = canceled.Cancel;
        var changed = reopened.Attributes.Select(item => new CharacterCreationAttributeAllocation(
            item.AttributeId, item.AttributeId == "BOD" ? 2 : 0, 0)).ToArray();
        var next = coordinator.PreviewCreationAttributes(reopened.Binding, changed).Value!;
        var interrupted = await coordinator.ConfirmCreationAttributesAsync(next, changed, canceled.Token);
        Require(interrupted is { Outcome: CharacterCreationFoundationOutcomes.Success, Receipt: not null, RefreshedState: null }
            && interrupted.Blockers.Contains("creation-attributes-post-commit-refresh-required"),
            "Cancellation after durable commit hid the saved receipt or claimed a refreshed page.");
        Require((await coordinator.ConfirmCreationAttributesAsync(next, changed)).Receipt is null && probe.ConfirmCalls == 2,
            "An interrupted post-commit refresh allowed mutation replay.");
        var interruptedSaved = ReadSaved();
        Require(interruptedSaved.ContentRevision == saved.ContentRevision + 1,
            "Cancellation after commit lost or duplicated the saved allocation.");
        await HydrateFinalizationOwnerAsync(runtime, owners, interruptedSaved);
        Require(coordinator.LoadCreationAttributes().Value!.Attributes.Single(item => item.AttributeId == "BOD").Current == 3,
            "The committed allocation did not recover after canceled refresh.");
        Console.WriteLine("PRIORITY_TABLE_ATTRIBUTES_ENTRY " + method + " linked=" + linked
            + " load/preview/save/reopen, forged inputs, owner ABA, replay and post-commit cancellation passed");
        if (!attributesOnly) await VerifyOwnerBoundSkillsAfterAttributesAsync(runtime, owners, linked, method);
    }

    private static async Task VerifyAttributesBackgroundPreviewAsync(NativeRewardRuntime runtime,
        ControlledLinkedOwner owners, AttributesCommitProbe probe, CharacterCreationAttributesState state,
        Func<WorkspaceStoredDocument> readSaved)
    {
        string savedDigest = FinalizationDocumentDigest(readSaved());
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            foreach (string scenario in new[] { "ready", "cancel", "leave", "draft-change", "owner-aba" })
            {
                var draft = new CreationAttributesPhoneDraft();
                draft.Bind(state, runtime.Coordinator.State);
                var changed = draft.ChangedAllocations(state, "BOD", 1, 0)!;
                var changedPreview = runtime.Coordinator.PreviewCreationAttributes(state.Binding, changed);
                var page = new CreationAttributeAllocationPage(runtime.Coordinator, draft, "BOD", state);
                var prepare = typeof(CreationAttributeAllocationPage).GetMethod("PrepareForAppearanceRefreshAsync",
                    BindingFlags.NonPublic | BindingFlags.Instance)!;
                Require(prepare.DeclaringType == typeof(CreationAttributeAllocationPage),
                    "Attribute Core previews must be prepared asynchronously before rendering.");
                var refresh = typeof(CreationAttributeAllocationPage).GetMethod("Refresh",
                    BindingFlags.NonPublic | BindingFlags.Instance)!;
                using var release = new ManualResetEventSlim();
                using var cancellation = new CancellationTokenSource();
                var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                int uiThread = Environment.CurrentManagedThreadId;
                int reads = 0;
                probe.BeforeRead = () =>
                {
                    Require(Environment.CurrentManagedThreadId != uiThread && owners.ActiveLeases == 0,
                        "Attribute reads/previews blocked the UI or carried an owner lease across await.");
                    Interlocked.Increment(ref reads);
                    entered.TrySetResult();
                    Require(release.Wait(TimeSpan.FromSeconds(10)), "Attribute test read was not released.");
                };
                Task pending = (Task)prepare.Invoke(page, [cancellation.Token])!;
                try
                {
                    await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    Require(!pending.IsCompleted, "Attribute preview did not retain the delayed read.");
                    var body = (VerticalStackLayout)((ScrollView)page.Content!).Content!;
                    Require(body.Children.OfType<Label>().Any(item => item.IsVisible
                        && item.AutomationId == "creation-attribute-allocation-loading"
                        && item.Text == CreationAllocationStrings.Get("AttributeAllocation.Checking", "Checking points…"))
                        && !body.Children.OfType<ActivityIndicator>().Any(item => item.IsRunning),
                        "Attribute loading must show readable static progress before the Core read finishes.");
                    var heartbeat = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    ui.Post(_ => heartbeat.SetResult(), null);
                    await heartbeat.Task.WaitAsync(TimeSpan.FromSeconds(2));
                    if (scenario == "cancel") cancellation.Cancel();
                    if (scenario == "leave") typeof(CreationAttributeAllocationPage)
                        .GetMethod("OnDisappearing", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(page, null);
                    if (scenario == "draft-change") Require(draft.TryAdopt(state,
                        runtime.Coordinator.State, changedPreview, changed), "SETUP: draft change rejected.");
                    if (scenario == "owner-aba")
                    {
                        var originalOwner = owners.Current;
                        owners.Set(ContactsOwnerB);
                        owners.Set(originalOwner);
                    }
                }
                finally { release.Set(); }
                try { await pending; }
                catch (OperationCanceledException) when (scenario is "cancel" or "leave") { }
                catch (InvalidOperationException error) when (scenario == "owner-aba"
                    && error.Message == "The Creation owner changed during its read.") { }
                finally { probe.BeforeRead = null; }
                int preparedReads = reads;
                // Rendering must not invoke Core, even without the thread probe.
                probe.BeforeRead = () => throw new Exception("Attribute Refresh re-entered Core.");
                try { refresh.Invoke(page, null); refresh.Invoke(page, null); }
                finally { probe.BeforeRead = null; }
                var rendered = (VerticalStackLayout)((ScrollView)page.Content!).Content!;
                bool enabled = rendered.Children.OfType<Button>().Any(item => item.IsEnabled);
                Require(enabled == (scenario == "ready") && reads == preparedReads,
                    "Canceled, disappeared or changed-draft preparation became actionable: " + scenario);
            }
        });
        Require(FinalizationDocumentDigest(readSaved()) == savedDigest,
            "Attribute preview preparation mutated the durable runner.");
        await HydrateFinalizationOwnerAsync(runtime, owners, readSaved());
        Console.WriteLine("PASS Attribute background previews, UI heartbeat, pure render, cancellation, disappearance, changed-draft and owner ABA rejection");
    }

    private static void VerifyAttributesLocalUndo(NativeRewardRuntime runtime,
        CharacterCreationAttributesState state, Func<WorkspaceStoredDocument> readSaved)
    {
        var coordinator = runtime.Coordinator;
        string savedDigest = FinalizationDocumentDigest(readSaved());
        foreach (bool karma in new[] { false, true })
        {
            var draft = new CreationAttributesPhoneDraft();
            draft.Bind(state, coordinator.State);
            var changed = draft.ChangedAllocations(state, "BOD", karma ? 0 : 1, karma ? 1 : 0)!;
            var preview = coordinator.PreviewCreationAttributes(state.Binding, changed);
            Require(draft.TryAdopt(state, coordinator.State, preview, changed),
                "SETUP: actual Core did not admit the uncommitted attribute increment.");
            var restored = draft.ChangedAllocations(state, "BOD", karma ? 0 : -1, karma ? -1 : 0)!;
            var duplicate = coordinator.PreviewCreationAttributes(state.Binding, restored);
            Require(duplicate.Value is { CanConfirm: false } value
                && value.Blockers.SequenceEqual(new[] { "creation-attributes-draft-duplicate" })
                && restored.SequenceEqual(state.PendingDraft!.Allocations),
                "SETUP: Core did not identify the unchanged saved allocation: "
                + JsonSerializer.Serialize(new { duplicate.Outcome, duplicate.Blockers,
                    duplicate.Value?.CanConfirm, PreviewBlockers = duplicate.Value?.Blockers }));
            Require(CreationAttributesPhoneAuthority.CanAdoptPreview(state, coordinator.State, duplicate, restored)
                && draft.TryAdopt(state, coordinator.State, duplicate, restored),
                "Local Attribute minus cannot restore the saved allocation after an uncommitted plus.");
            Require(draft.Allocations(state).SequenceEqual(state.PendingDraft!.Allocations)
                && draft.Attribute(state, "BOD")!.Current == state.Attributes.Single(x => x.AttributeId == "BOD").Current
                && draft.NormalBudget(state) == state.NormalPointBudget
                && draft.KarmaBudget(state) == state.CreationKarmaBudget,
                "Local Attribute undo did not restore exact saved values and budgets.");
            var original = duplicate.Value!;
            Require(!CreationAttributesPhoneAuthority.CanConfirmPreview(state, coordinator.State, original, restored),
                "Local undo must not authorize duplicate persistence.");
            foreach (var invalid in new[]
            {
                duplicate with { Outcome = CharacterCreationFoundationOutcomes.Success },
                duplicate with { Blockers = [] },
                duplicate with { Blockers = ["creation-attributes-draft-duplicate", "another-blocker"] },
                duplicate with { Value = original with { Blockers = [] } },
                duplicate with { Value = original with { CanConfirm = true } },
                duplicate with { Value = original with { RequiresExplicitConfirmation = false } },
                duplicate with { Value = original with { Binding = original.Binding with { ContentRevision = original.Binding.ContentRevision + 1 } } },
                duplicate with { Value = original with { NormalPointBudget = original.NormalPointBudget with { Total = original.NormalPointBudget.Total + 1, Remaining = original.NormalPointBudget.Remaining + 1 } } },
                duplicate with { Value = original with { Attributes = original.Attributes.Select(x => x.AttributeId == "BOD" ? x with { Current = x.Current + 1 } : x).ToArray() } }
            })
            {
                Require(!CreationAttributesPhoneAuthority.CanAdoptPreview(state, coordinator.State, invalid, restored)
                    && !draft.TryAdopt(state, coordinator.State, invalid, restored),
                    "Local undo admitted a noncanonical or additionally blocked saved projection.");
            }
            Require(!CreationAttributesPhoneAuthority.CanAdoptPreview(state, coordinator.State, duplicate, changed)
                && !CreationAttributesPhoneAuthority.CanAdoptPreview(state,
                    coordinator.State with { WorkspaceId = null }, duplicate, restored),
                "Local undo ignored changed allocations or stale workspace identity.");
        }
        Require(FinalizationDocumentDigest(readSaved()) == savedDigest,
            "Local Attribute plus/minus changed durable state without confirmation.");
        Console.WriteLine("ATTRIBUTES_LOCAL_UNDO priority/karma, exact saved projection, duplicate-save and forged-state guards passed");
    }

    private static async Task VerifyOwnerBoundSkillsAfterAttributesAsync(NativeRewardRuntime runtime,
        ControlledLinkedOwner owners, bool linked, string method)
    {
        var coordinator = runtime.Coordinator;
        var owner = owners.Current;
        WorkspaceStoredDocument ReadSaved()
        {
            var store = new FileWorkspaceStore(runtime.StateDirectory);
            return (owner.IsLocalSingleUser ? store.Get(runtime.Id) : store.Get(owner, runtime.Id)).Value!;
        }
        var before = ReadSaved();
        var loaded = coordinator.LoadCreationSkills();
        Require(loaded.Value is { CanEdit: true } && CreationSkillsPhoneAuthority.IsReady(loaded.Value, coordinator.State),
            "Actual phone Skills unavailable after confirmed Attributes: " + JsonSerializer.Serialize(loaded));
        var state = loaded.Value!;
        var language = state.Authority.KnowledgeSkills.First(x => x.CanBeNativeLanguage);
        var running = CreationSkillsPhoneAuthority.AvailableActiveSkills(state).Single(x => x.Name == "Running");
        CharacterCreationSkillAllocation[] allocations =
        [new(language.SourceSkillId, CharacterCreationSkillKinds.Knowledge, null, null, true),
         new(running.SourceSkillId, CharacterCreationSkillKinds.Active, 1, null, false)];
        var preview = coordinator.PreviewCreationSkills(state.Binding, allocations, []).Value!;
        Require(preview is { CanConfirm: true }, "Native Skills did not admit a legal real-source choice.");
        string key = CreationSkillsPhoneAuthority.ComputeIdempotencyKey(preview, allocations, []);
        Require((await coordinator.ConfirmCreationSkillsAsync(preview with { }, allocations, [], key)).Receipt is null,
            "Skills accepted a copied unissued preview.");
        Require((await coordinator.ConfirmCreationSkillsAsync(preview, allocations, [], key + "altered")).Receipt is null,
            "Skills accepted a key different from its exact reviewed command.");
        owners.Set(ContactsOwnerB);
        owners.Set(owner);
        Require(!coordinator.IsCreationSkillsStateCurrent(state)
            && !coordinator.CanDisplayCreationSkillsPreview(preview)
            && coordinator.RevalidateCreationSkills(state).Value is null
            && (await coordinator.ConfirmCreationSkillsAsync(preview, allocations, [], key)).Receipt is null,
            "Owner ABA revived an old Skills page or operation.");
        Require(FinalizationDocumentDigest(before) == FinalizationDocumentDigest(ReadSaved()),
            "Rejected Skills operations changed saved data.");
        await HydrateFinalizationOwnerAsync(runtime, owners, before);
        state = coordinator.LoadCreationSkills().Value!;
        preview = coordinator.PreviewCreationSkills(state.Binding, allocations, []).Value!;
        key = CreationSkillsPhoneAuthority.ComputeIdempotencyKey(preview, allocations, []);
        var saved = await coordinator.ConfirmCreationSkillsAsync(preview, allocations, [], key);
        Require(saved is { Outcome: CharacterCreationFoundationOutcomes.Success, Receipt: not null,
            RefreshedState: not null, Blockers.Count: 0 }, "Skills confirmation failed: " + JsonSerializer.Serialize(saved));
        Require(coordinator.IsCreationSkillsReceiptCurrent(saved.Receipt!)
            && coordinator.IsCreationSkillsStateCurrent(saved.RefreshedState!), "Fresh Skills receipt lost its original owner.");
        Require((await coordinator.ConfirmCreationSkillsAsync(preview, allocations, [], key)).Receipt is null,
            "Old Skills confirmation was replayed.");
        var after = ReadSaved();
        Require(after.ContentRevision == before.ContentRevision + 1 && after.SavedRevision == after.ContentRevision
            && after.Document.Content == before.Document.Content, "Skills lost checkpoint atomicity or changed live XML.");
        await HydrateFinalizationOwnerAsync(runtime, owners, after);
        var reopened = coordinator.LoadCreationSkills().Value!;
        Require(reopened.Skills.Single(x => x.SourceSkillId == running.SourceSkillId).Rating == 1
            && reopened.Skills.Single(x => x.SourceSkillId == language.SourceSkillId).IsNativeLanguage
            && coordinator.IsCreationSkillsStateCurrent(reopened) && owners.ActiveLeases == 0,
            "Actual Skills did not survive cold-store reopen.");
        if (linked) Require(!new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Success,
            "Skills created a hidden local copy.");
        Console.WriteLine("PRIORITY_TABLE_SKILLS_ENTRY " + method + " linked=" + linked
            + " actual preview/save/cold-reopen, forged inputs, owner ABA, no hidden local copy and no replay passed");
        await VerifyOwnerBoundQualitiesAfterSkillsAsync(runtime, owners, linked, method);
    }

    private static async Task VerifyOwnerBoundQualitiesAfterSkillsAsync(NativeRewardRuntime runtime,
        ControlledLinkedOwner owners, bool linked, string method)
    {
        var coordinator = runtime.Coordinator;
        var owner = owners.Current;
        WorkspaceStoredDocument ReadSaved()
        {
            var store = new FileWorkspaceStore(runtime.StateDirectory);
            return (owner.IsLocalSingleUser ? store.Get(runtime.Id) : store.Get(owner, runtime.Id)).Value!;
        }
        var before = ReadSaved();
        var original = coordinator.State;
        Require(original.CreationQualities is { } projected && CreationQualitiesPhoneAuthority.IsReady(projected, original),
            "Production overview did not expose owner-only Qualities.");
        var loaded = await coordinator.LoadCreationQualitiesForDisplayAsync(original, CancellationToken.None);
        Require(loaded.Value is { CanEdit: true }, "Owner-only Qualities unavailable: " + JsonSerializer.Serialize(loaded));
        var preview = coordinator.PreviewCreationQualities(loaded.Value!.Binding, [], original).Value!;
        Require(preview is { CanConfirm: true }, "An empty valid Qualities draft was blocked.");
        var reviewed = CharacterCreationQualitiesCheckpoint.CreateReviewed(preview, [], Guid.NewGuid());
        var checkpoints = CharacterCreationQualitiesCheckpointStore.CreateDefault(original.DisplayOwnerContext,
            coordinator.IsCreationQualitiesOwnerCurrent);
        Require(checkpoints.TryCreate(reviewed, out reviewed, out var blocker), blocker);
        Require(checkpoints.TryBeginApply(CharacterCreationQualitiesCheckpointCas.From(reviewed), out var applying, out blocker), blocker);
        var gate = (SemaphoreSlim)typeof(RunnerSessionCoordinator).GetField("_workspaceActivationGate",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(coordinator)!;
        await gate.WaitAsync();
        Task<CreationQualitiesPhoneConfirmResult> pending;
        try
        {
            pending = coordinator.ConfirmCreationQualitiesAsync(applying, display: original);
            owners.Set(ContactsOwnerB);
            var other = CharacterCreationQualitiesCheckpointStore.CreateDefault(owners.Capture(),
                stamp => stamp == owners.Capture());
            Require(!other.TryRead(out _, out blocker) && string.IsNullOrEmpty(blocker),
                "Another account could see the original Qualities checkpoint.");
            owners.Set(owner);
        }
        finally { gate.Release(); }
        Require((await pending).Receipt is null && coordinator.LoadCreationQualities(original).Value is null
            && coordinator.PreviewCreationQualities(preview.Binding, [], original).Value is null,
            "Queued confirmation or preview survived owner ABA.");
        Require(FinalizationDocumentDigest(before) == FinalizationDocumentDigest(ReadSaved()),
            "Rejected Qualities command changed the stored runner.");
        Require(!checkpoints.TryRead(out _, out blocker) && !string.IsNullOrEmpty(blocker),
            "Old checkpoint accessor survived owner ABA.");
        await HydrateFinalizationOwnerAsync(runtime, owners, before);
        original = coordinator.State;
        var freshStore = CharacterCreationQualitiesCheckpointStore.CreateDefault(original.DisplayOwnerContext,
            coordinator.IsCreationQualitiesOwnerCurrent);
        Require(freshStore.TryRead(out var recovered, out blocker) && recovered.CheckpointDigest == applying.CheckpointDigest,
            "A new display of the same account could not recover the exact applying command.");
        var saved = await coordinator.ConfirmCreationQualitiesAsync(recovered, display: original);
        Require(saved is { Outcome: CreationQualitiesPhoneOutcomes.Applied, MutationOutcomeKnown: true,
            Receipt: not null, RefreshedState: not null, Blockers.Count: 0 },
            "Owner-bound Qualities save failed: " + JsonSerializer.Serialize(saved));
        Require(freshStore.TryRecordApplied(CharacterCreationQualitiesCheckpointCas.From(recovered), saved.Receipt!,
            out var applied, out blocker), blocker);
        var after = ReadSaved();
        Require(after.ContentRevision == before.ContentRevision + 1 && after.SavedRevision == after.ContentRevision
            && after.Document.Content == before.Document.Content && owners.ActiveLeases == 0,
            "Qualities did not make exactly one auxiliary checkpoint.");
        await HydrateFinalizationOwnerAsync(runtime, owners, after);
        var cold = coordinator.LoadCreationQualities().Value!;
        Require(CreationQualitiesPhoneAuthority.ReceiptMatchesPersistedState(recovered, saved.Receipt!, cold),
            "Qualities receipt did not survive cold-store reopen.");
        var replay = await coordinator.ConfirmCreationQualitiesAsync(recovered, display: coordinator.State);
        Require(replay.Receipt?.ReceiptDigest == saved.Receipt!.ReceiptDigest
            && FinalizationDocumentDigest(after) == FinalizationDocumentDigest(ReadSaved()),
            "Exact interrupted-command recovery duplicated the Qualities mutation.");
        Require(freshStore.TryAcknowledgeApplied(CharacterCreationQualitiesCheckpointCas.From(applied), out blocker), blocker);
        if (linked) Require(!new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Success,
            "Qualities created a hidden legacy workspace.");
        Console.WriteLine("PRIORITY_TABLE_QUALITIES_ENTRY " + method + " linked=" + linked
            + " production projection, save/cold-reopen, queued ABA rejection, scoped checkpoint and idempotent recovery passed");
        await VerifyOwnerBoundPurchasesAsync(runtime, owners, linked, method);
    }


    public static async Task RunCreationPurchaseRefreshAsync(string contentRoot)
    {
        foreach (string method in new[] { CharacterCreationBuildMethods.Priority, CharacterCreationBuildMethods.SumToTen })
        foreach (bool gearPurchase in new[] { false, true })
        foreach (string scenario in new[] { "saved", "shell-error", "shell-owner-aba", "shell-cancellation" })
        {
            var owners = new ControlledLinkedOwner();
            owners.Set(OwnerScope.LocalSingleUser);
            using var cancel = new CancellationTokenSource();
            int shellLists = 0;
            bool observingCommit = false;
            await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners,
                productionCreationOverview: true, beforeShellWorkspaceList: () =>
                {
                    shellLists++;
                    if (!observingCommit) return;
                    if (scenario == "shell-error") throw new IOException("Synthetic purchase shell failure.");
                    if (scenario == "shell-owner-aba")
                    { owners.Set(ContactsOwnerB); owners.Set(OwnerScope.LocalSingleUser); }
                    if (scenario == "shell-cancellation") cancel.Cancel();
                });
            var before = PrepareActualFinalizationReadyContext(runtime, buildMethod: method, stopBeforeGear: true);
            await HydrateFinalizationOwnerAsync(runtime, owners, before);
            var coordinator = runtime.Coordinator;
            var original = coordinator.State;
            string receiptDigest;
            Func<Task<bool>> replayHasReceipt;
            Func<string?> coldReceipt;
            var timer = new System.Diagnostics.Stopwatch();
            shellLists = 0;
            if (gearPurchase)
            {
                var presenter = new CharacterCreationGearInteractionPresenter(
                    runtime.Services.GetRequiredService<ICharacterCreationGearService>(),
                    runtime.Services.GetRequiredService<IOwnerBoundCharacterCreationGearService>());
                var loaded = presenter.Load(original).State!;
                var item = loaded.Authority.Options.First(option => option.Name == "Flashlight" && option.IsSelectable);
                var preview = presenter.Prepare(original, [new(item.OptionId, 1)]).PreparedPreview!;
                Require(preview is not null, "SETUP: missing actual purchase preview.");
                observingCommit = true;
                timer.Start();
                var result = await coordinator.ConfirmCreationGearPurchaseAsync(presenter, original, preview!, cancel.Token);
                timer.Stop();
                Require(result.Receipt is not null && result.Outcome == CharacterCreationGearOutcomes.Applied,
                    "Gear refresh lost its durable receipt: " + JsonSerializer.Serialize(result));
                Require(scenario == "saved" ? result.RefreshedState is not null && result.Blockers.Count == 0
                    : result.RefreshedState is null && result.Blockers.Contains(CharacterCreationGearInteractionBlockers.RefreshAuthorityRequired),
                    "Gear refresh published stale authority or failed a current reload: " + scenario);
                receiptDigest = result.Receipt!.ReceiptDigest;
                replayHasReceipt = async () => (await coordinator.ConfirmCreationGearPurchaseAsync(presenter, original, preview!)).Receipt is not null;
                coldReceipt = () => presenter.LookupReceipt(coordinator.State, preview!.IdempotencyKey).Receipt?.ReceiptDigest;
            }
            else
            {
                var presenter = new CharacterCreationResourcesInteractionPresenter(
                    runtime.Services.GetRequiredService<ICharacterCreationResourcesService>(),
                    runtime.Services.GetRequiredService<IOwnerBoundCharacterCreationResourcesService>());
                var loaded = presenter.Load(original).State!;
                var option = loaded.Options.Single(item => item.KarmaInvestment == 1 && item.IsEnabled);
                var preview = presenter.Prepare(original, option.OptionId).PreparedPreview!;
                Require(preview is not null, "SETUP: missing actual Resources preview.");
                observingCommit = true;
                timer.Start();
                var result = await coordinator.ConfirmCreationResourcesPurchaseAsync(presenter, original, preview!, cancel.Token);
                timer.Stop();
                Require(result.Receipt is not null && result.Outcome == CharacterCreationResourcesOutcomes.Applied,
                    "Resources refresh lost its durable receipt: " + JsonSerializer.Serialize(result));
                Require(scenario == "saved" ? result.RefreshedState is not null && result.Blockers.Count == 0
                    : result.RefreshedState is null && result.Blockers.Contains(CharacterCreationResourcesInteractionBlockers.RefreshAuthorityRequired),
                    "Resources refresh published stale authority or failed a current reload: " + scenario);
                receiptDigest = result.Receipt!.ReceiptDigest;
                replayHasReceipt = async () => (await coordinator.ConfirmCreationResourcesPurchaseAsync(presenter, original, preview!)).Receipt is not null;
                coldReceipt = () => presenter.LookupReceipt(coordinator.State, preview!.IdempotencyKey).Receipt?.ReceiptDigest;
            }
            observingCommit = false;
            Console.WriteLine($"PURCHASE_REFRESH {method} {(gearPurchase ? "Gear" : "Resources")} {scenario}: shellLists={shellLists}, elapsedMs={timer.ElapsedMilliseconds}");
            Require(shellLists == 1, "A purchase must synchronize the shell once, not enumerate/reopen the roster twice.");
            var after = new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!;
            Require(after.ContentRevision == before.ContentRevision + 1 && after.SavedRevision == after.ContentRevision
                && after.Document.Content == before.Document.Content && owners.ActiveLeases == 0,
                "Purchase refresh lost its checkpoint, changed XML or retained an owner lease.");
            Require(!await replayHasReceipt(), "Purchase refresh allowed the retained command to be submitted again.");
            await HydrateFinalizationOwnerAsync(runtime, owners, after);
            Require(coldReceipt() == receiptDigest, "Purchase receipt was lost after reopening under the current owner.");
            RequireSameRewardDocument(after, new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!);
            Console.WriteLine("PASS purchase single shell sync, durable receipt, cold reopen and no replay: " + scenario);
        }
    }

    private static async Task VerifyOwnerBoundPurchasesAsync(NativeRewardRuntime runtime,
        ControlledLinkedOwner owners, bool linked, string method)
    {
        var coordinator = runtime.Coordinator;
        var owner = owners.Current;
        WorkspaceStoredDocument ReadSaved()
        {
            var store = new FileWorkspaceStore(runtime.StateDirectory);
            return (owner.IsLocalSingleUser ? store.Get(runtime.Id) : store.Get(owner, runtime.Id)).Value!;
        }
        var before = ReadSaved();
        var original = coordinator.State;
        var resourceProbe = new ResourcesCommitProbe(runtime.Services.GetRequiredService<IOwnerBoundCharacterCreationResourcesService>());
        var resources = new CharacterCreationResourcesInteractionPresenter(
            runtime.Services.GetRequiredService<ICharacterCreationResourcesService>(), resourceProbe);
        var state = resources.Load(original).State!;
        Require(state is not null && CreationResourcesPhoneAuthority.IsReady(state, original),
            "Owner-only Resources are unavailable.");
        Require(new CharacterCreationResourcesInteractionPresenter(
                runtime.Services.GetRequiredService<ICharacterCreationResourcesService>()).Load(original).State is null,
            "Scoped display silently used a legacy-only Resources presenter.");
        var option = state!.Options.Single(item => item.KarmaInvestment == 1 && item.IsEnabled);
        var prepared = resources.Prepare(original, option.OptionId).PreparedPreview!;
        Require(prepared is not null && CreationResourcesPhoneAuthority.PreparedMatches(prepared, state, original),
            "Resources did not prepare an exact affordable conversion.");
        Require(resources.Confirm(original, new(prepared! with { }, prepared!.PreviewDigest, prepared.IdempotencyKey, true)).Receipt is null
            && resources.Confirm(original, new(prepared, prepared.PreviewDigest, prepared.IdempotencyKey, false)).Receipt is null
            && resources.Confirm(original, new(prepared, "sha256:" + new string('0', 64), prepared.IdempotencyKey, true)).Receipt is null,
            "Resources accepted a copied envelope, missing consent or forged digest.");
        var gate = (SemaphoreSlim)typeof(RunnerSessionCoordinator).GetField("_workspaceActivationGate",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(coordinator)!;
        await gate.WaitAsync();
        Task<CharacterCreationResourcesInteractionConfirmResult> queued;
        try
        {
            queued = coordinator.ConfirmCreationResourcesPurchaseAsync(resources, original, prepared);
            owners.Set(ContactsOwnerB); owners.Set(owner);
        }
        finally { gate.Release(); }
        Require((await queued).Receipt is null && !coordinator.CanDisplayCreationPurchase(original)
            && resources.Load(original).State is null
            && FinalizationDocumentDigest(before) == FinalizationDocumentDigest(ReadSaved()),
            "Resources crossed a queued owner ABA or changed the saved runner.");
        await HydrateFinalizationOwnerAsync(runtime, owners, before);
        original = coordinator.State;
        prepared = resources.Prepare(original, option.OptionId).PreparedPreview!;
        using var canceled = new CancellationTokenSource();
        resourceProbe.AfterConfirm = () => { resourceProbe.FailNextLoad = true; canceled.Cancel(); };
        var saved = await coordinator.ConfirmCreationResourcesPurchaseAsync(resources, original, prepared, canceled.Token);
        Require(saved is { Outcome: CharacterCreationResourcesOutcomes.Applied, Receipt: not null, RefreshedState: null }
            && saved.Blockers.Contains(CharacterCreationResourcesInteractionBlockers.RefreshAuthorityRequired),
            "Resources postcommit cancellation hid the durable receipt: " + JsonSerializer.Serialize(saved));
        Require((await coordinator.ConfirmCreationResourcesPurchaseAsync(resources, original, prepared)).Receipt is null
            && resourceProbe.ConfirmCalls == 1, "Resources canceled refresh allowed another confirm.");
        var afterResources = ReadSaved();
        Require(afterResources.ContentRevision == before.ContentRevision + 1
            && afterResources.Document.Content == before.Document.Content && owners.ActiveLeases == 0,
            "Resources checkpoint was lost, duplicated or mutated live character XML.");
        await HydrateFinalizationOwnerAsync(runtime, owners, afterResources);
        original = coordinator.State;
        var coldResources = new CharacterCreationResourcesInteractionPresenter(
            runtime.Services.GetRequiredService<ICharacterCreationResourcesService>(),
            runtime.Services.GetRequiredService<IOwnerBoundCharacterCreationResourcesService>());
        Require(coldResources.LookupReceipt(original, prepared.IdempotencyKey).Receipt?.ReceiptDigest == saved.Receipt!.ReceiptDigest
            && CreationResourcesPhoneAuthority.RefreshedStateMatches(prepared, saved.Receipt!, coldResources.Load(original).State!),
            "Resources receipt/draft did not survive cold-store reopen.");

        var gear = new CharacterCreationGearInteractionPresenter(
            runtime.Services.GetRequiredService<ICharacterCreationGearService>(),
            runtime.Services.GetRequiredService<IOwnerBoundCharacterCreationGearService>());
        var gearLoad = await coordinator.LoadCreationGearForDisplayAsync(gear, original, default);
        Require(gearLoad.Ready && gearLoad.Load.State is not null, "Owner-only Gear catalog unavailable.");
        var gearState = gearLoad.Load.State!;
        var flashlight = gearState.Authority.Options.First(item => item.Name == "Flashlight" && item.IsSelectable);
        CharacterCreationGearSelection[] basket = [new(flashlight.OptionId, 1)];
        var gearPreview = gear.Prepare(original, basket).PreparedPreview!;
        Require(gearPreview is not null && gearPreview.Preview.BudgetAfter.BasketCost > 0
            && CreationGearPhoneAuthority.PreparedMatches(gearPreview, gearState, original), "Actual Gear purchase unavailable.");
        Require(gear.Confirm(original, new(gearPreview! with { }, gearPreview!.Preview.PreviewDigest, gearPreview.IdempotencyKey, true)).Receipt is null
            && gear.Confirm(original, new(gearPreview, gearPreview.Preview.PreviewDigest, gearPreview.IdempotencyKey, false)).Receipt is null,
            "Gear accepted an unissued envelope or missing consent.");
        await gate.WaitAsync();
        Task<CharacterCreationGearInteractionConfirmResult> gearQueued;
        try
        {
            gearQueued = coordinator.ConfirmCreationGearPurchaseAsync(gear, original, gearPreview);
            owners.Set(ContactsOwnerB); owners.Set(owner);
        }
        finally { gate.Release(); }
        Require((await gearQueued).Receipt is null && gear.Load(original).State is null
            && FinalizationDocumentDigest(afterResources) == FinalizationDocumentDigest(ReadSaved()), "Gear crossed an owner ABA.");
        await HydrateFinalizationOwnerAsync(runtime, owners, afterResources);
        original = coordinator.State;
        gearPreview = gear.Prepare(original, basket).PreparedPreview!;
        var purchased = await coordinator.ConfirmCreationGearPurchaseAsync(gear, original, gearPreview);
        Require(purchased is { Outcome: CharacterCreationGearOutcomes.Applied, Receipt: not null, RefreshedState: not null, Blockers.Count: 0 },
            "Native Gear purchase failed: " + JsonSerializer.Serialize(purchased));
        var after = ReadSaved();
        Require(after.ContentRevision == afterResources.ContentRevision + 1 && after.SavedRevision == after.ContentRevision
            && after.Document.Content == before.Document.Content && owners.ActiveLeases == 0,
            "Gear purchase did not make exactly one auxiliary checkpoint.");
        Require((await coordinator.ConfirmCreationGearPurchaseAsync(gear, original, gearPreview)).Receipt is null
            && FinalizationDocumentDigest(after) == FinalizationDocumentDigest(ReadSaved()), "Gear confirmation replayed.");
        await HydrateFinalizationOwnerAsync(runtime, owners, after);
        var coldGear = new CharacterCreationGearInteractionPresenter(
            runtime.Services.GetRequiredService<ICharacterCreationGearService>(),
            runtime.Services.GetRequiredService<IOwnerBoundCharacterCreationGearService>());
        Require(coldGear.LookupReceipt(coordinator.State, gearPreview.IdempotencyKey).Receipt?.ReceiptDigest
                == purchased.Receipt!.ReceiptDigest
            && CreationGearPhoneAuthority.RefreshedStateMatches(gearPreview, purchased.Receipt!, coldGear.Load(coordinator.State).State!),
            "Purchased Gear did not cold-reopen with its exact receipt.");
        if (linked) Require(!new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Success,
            "Purchases created a hidden legacy runner.");
        Console.WriteLine("PRIORITY_TABLE_PURCHASES " + method + " linked=" + linked
            + " conversion, real purchase, queued ABA, forged preview, save/cold reopen and postcommit cancellation passed");
    }

    private sealed class ResourcesCommitProbe(IOwnerBoundCharacterCreationResourcesService inner)
        : IOwnerBoundCharacterCreationResourcesService
    {
        public Action? AfterConfirm { get; set; }
        public bool FailNextLoad { get; set; }
        public int ConfirmCalls { get; private set; }
        public CharacterCreationResourcesResult<CharacterCreationResourcesState> Load(OwnerContextStamp owner,
            CharacterCreationResourcesLoadRequest request)
        {
            if (FailNextLoad) { FailNextLoad = false; throw new IOException("Injected postcommit read failure."); }
            return inner.Load(owner, request);
        }
        public CharacterCreationResourcesResult<CharacterCreationResourcesPreview> Preview(OwnerContextStamp owner,
            CharacterCreationResourcesPreviewRequest request) => inner.Preview(owner, request);
        public CharacterCreationResourcesResult<CharacterCreationResourcesReceipt> Confirm(OwnerContextStamp owner,
            CharacterCreationResourcesConfirmRequest request)
        {
            ConfirmCalls++;
            var result = inner.Confirm(owner, request);
            AfterConfirm?.Invoke();
            return result;
        }
        public CharacterCreationResourcesResult<CharacterCreationResourcesReceipt> LookupReceipt(OwnerContextStamp owner,
            CharacterCreationResourcesReceiptLookupRequest request) => inner.LookupReceipt(owner, request);
    }
    private sealed class AttributesCommitProbe(IOwnerBoundCharacterCreationAttributesService inner)
        : IOwnerBoundCharacterCreationAttributesService
    {
        public Action? AfterConfirm { get; set; }
        public Action? BeforeRead { get; set; }
        public int LoadCalls { get; private set; }
        public int PreviewCalls { get; private set; }
        public int ConfirmCalls { get; private set; }
        public CharacterCreationFoundationResult<CharacterCreationAttributesState> Load(OwnerContextStamp owner,
            CharacterCreationAttributesLoadRequest request)
        { LoadCalls++; BeforeRead?.Invoke(); return inner.Load(owner, request); }
        public CharacterCreationFoundationResult<CharacterCreationAttributesPreview> Preview(OwnerContextStamp owner,
            CharacterCreationAttributesPreviewRequest request)
        { PreviewCalls++; BeforeRead?.Invoke(); return inner.Preview(owner, request); }
        public CharacterCreationFoundationResult<CharacterCreationAttributesReceipt> Confirm(OwnerContextStamp owner,
            CharacterCreationAttributesConfirmRequest request)
        {
            ConfirmCalls++;
            var result = inner.Confirm(owner, request);
            AfterConfirm?.Invoke();
            return result;
        }
    }

    public static async Task RunPriorityRacialFinalizationAsync(string contentRoot)
    {
        if (!Path.IsPathFullyQualified(contentRoot) || !Directory.Exists(Path.Combine(contentRoot, "data")))
            throw new ArgumentException("Supply the explicit canonical Core content root.", nameof(contentRoot));
        await RunPriorityChoiceGuidanceAsync(contentRoot);
        foreach (string method in new[] { CharacterCreationBuildMethods.Priority, CharacterCreationBuildMethods.SumToTen })
        foreach (string metatype in new[] { "Elf", "Ork" })
            await RunCreationFinalizationLocalBaselineAsync(contentRoot, method, metatype);
    }

    private static async Task RunCreationFinalizationLocalBaselineAsync(string contentRoot,
        string method = CharacterCreationBuildMethods.Priority, string metatype = "Human")
    {
        var owners = new ControlledLinkedOwner();
        var uiContext = new SynchronizationContext();
        FinalizationThreadProbe? threadProbe = null;
        await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners, creationFinalization: true,
            finalizationDecorator: actual => threadProbe = new FinalizationThreadProbe(actual, uiContext));
        Require(owners.Current == OwnerScope.LocalSingleUser, "Local positive control did not retain the trusted local scope.");
        var before = PrepareActualFinalizationReadyContext(runtime, buildMethod: method, metatypeName: metatype);
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
        AssertFinalReviewNames(runtime.Coordinator, review.Value!);
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
        if (metatype is "Elf" or "Ork")
        {
            XElement savedCharacter = XDocument.Parse(cold.Document.Content).Root!;
            XElement[] racial = savedCharacter.Element("qualities")!.Elements("quality")
                .Where(item => item.Element("name")?.Value == "Low-Light Vision").ToArray();
            Require(savedCharacter.Element("metatype")?.Value == metatype
                && racial.Length == 1 && racial[0].Element("qualitysource")?.Value == "Metatype"
                && review.Value!.OrderedDeltas.Any(delta => delta.TargetName == "Low-Light Vision"
                    && delta.KarmaCost == 0),
                "Native finalization lost, duplicated or charged for the exact racial quality.");
        }
        threadProbe!.RequireOffContextLoads = true;
        var retained = await StartFromUiContext(uiContext, () =>
            runtime.Coordinator.LoadPersistedPriorityTableCreationReceiptAsync(runtime.Coordinator.State, default));
        Require(applied.Value!.BuildMethod == method
            && retained?.ReceiptDigest == applied.Value.ReceiptDigest,
            "The native Career route must retain the exact method's persisted receipt.");
        await HydrateFinalizationOwnerAsync(runtime, owners, cold, expectedCreated: true);
        var originalDisplay = runtime.Coordinator.State;
        retained = await StartFromUiContext(uiContext, () =>
            runtime.Coordinator.LoadPersistedPriorityTableCreationReceiptAsync(originalDisplay, default));
        Require(retained?.ReceiptDigest == applied.Value.ReceiptDigest,
            "A fresh native display from the cold store lost the finalization receipt.");
        int loadsBeforeRender = threadProbe!.LoadCalls;
        var careerPage = new BuildPage(runtime.Coordinator);
        await StartFromUiContext(uiContext, () =>
        {
            typeof(BuildPage).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(careerPage, null);
            return Task.FromResult(true);
        });
        Require(threadProbe.LoadCalls == loadsBeforeRender,
            "Rendering the Career route synchronously reentered Core finalization on the UI thread.");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            try
            {
                await runtime.Coordinator.LoadPersistedPriorityTableCreationReceiptAsync(originalDisplay, cancelled.Token);
                throw new InvalidOperationException("Canceled receipt load was admitted.");
            }
            catch (OperationCanceledException) { }
        }
        Require(threadProbe.LoadCalls == loadsBeforeRender, "Canceled receipt load entered Core.");
        threadProbe.AfterLoad = () =>
        {
            owners.Set(ContactsOwnerB);
            owners.Set(OwnerScope.LocalSingleUser);
        };
        Require(await runtime.Coordinator.LoadPersistedPriorityTableCreationReceiptAsync(originalDisplay, default) is null,
            "A read completed across an owner transition must not become display authority.");
        loadsBeforeRender = threadProbe.LoadCalls;
        Require(!runtime.Coordinator.IsPersistedCreationReceiptDisplayCurrent(originalDisplay)
            && await runtime.Coordinator.LoadPersistedPriorityTableCreationReceiptAsync(originalDisplay, default) is null
            && threadProbe.LoadCalls == loadsBeforeRender,
            "A prior account generation must not display or reload a retained receipt after A->B->A.");
        Console.WriteLine("FINALIZATION_LOCAL_BASELINE " + JsonSerializer.Serialize(new
        {
            applied.Outcome, applied.Value.BuildMethod, metatype, before.ContentRevision, before.SavedRevision,
            afterContentRevision = cold.ContentRevision, afterSavedRevision = cold.SavedRevision,
            receiptDigest = applied.Value!.ReceiptDigest,
            beforeDigest = FinalizationDocumentDigest(before), afterDigest = FinalizationDocumentDigest(cold)
        }));
        Console.WriteLine("PASS actual local native/Core finalization baseline: " + method + "/" + metatype);
    }

    private static void AssertFinalReviewNames(RunnerSessionCoordinator coordinator, CharacterCreationFinalizationReview review)
    {
        string original = JsonSerializer.Serialize(review);
        Require(review.OrderedDeltas.Any(delta => delta.Kind == CharacterCreationFinalizationDeltaKinds.Skill
            && !string.IsNullOrWhiteSpace(delta.TargetName)), "Actual Core review has no canonical skill names.");
        var page = new CreationFinalizationPage(coordinator, review);
        typeof(CreationFinalizationPage).GetField("_visible", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(page, true);
        var refresh = typeof(CreationFinalizationPage).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!;
        refresh.Invoke(page, null);
        var body = (VerticalStackLayout)((ScrollView)page.Content!).Content!;
        var binding = body.Children.OfType<Label>().Single(label => label.AutomationId == "creation-finalization-binding");
        Require(!binding.IsVisible, "Final-review hashes should start hidden.");
        var labels = body.Children.OfType<Border>().Select(border => border.Content)
            .OfType<VerticalStackLayout>().SelectMany(card => card.Children.OfType<Label>()).ToArray();
        foreach (var delta in review.OrderedDeltas)
        {
            var target = labels.Single(label => label.AutomationId == "creation-finalization-target-" + delta.Order);
            Require(target.Text == CreationFinalizationPage.TargetLabel(delta) && target.IsVisible,
                "Final review hid or replaced a reviewed target name.");
            if (delta.TargetId is "starting-cash-dice" or "lifestyle" or "nuyen" or "karma")
                Require(target.Text != delta.TargetId, "Exact finalization field needs readable copy: " + delta.Kind + "/" + delta.TargetId);
            if (!string.IsNullOrWhiteSpace(delta.TargetName) && delta.Kind != CharacterCreationFinalizationDeltaKinds.Lifecycle)
                Require(target.Text == delta.TargetName, "Final review did not display the exact admitted name.");
            var deltaCard = (VerticalStackLayout)body.Children.OfType<Border>()
                .Single(border => border.AutomationId == "creation-finalization-delta-" + delta.Order).Content!;
            Require(deltaCard.Children.OfType<Label>().Any(label => label.IsVisible && label.Text == CreationFinalizationPage.ChangeLabel(delta)),
                "Final review hid a reviewed value change.");
            if (delta.KarmaCost != 0 || delta.NuyenCost != 0)
                Require(deltaCard.Children.OfType<Label>().Any(label => label.IsVisible && label.Text ==
                    $"Karma {delta.KarmaCost.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)} · Nuyen {delta.NuyenCost.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}"),
                    "Final review hid or changed an exact cost.");
            if (target.Text != delta.TargetId)
                Require(labels.Any(label => label.AutomationId == "creation-finalization-source-" + delta.Order
                    && !label.IsVisible && label.Text.Split('\n')[0] == delta.TargetId),
                    "A readable name removed its exact typed identity from diagnostics.");
        }
        var toggle = body.Children.OfType<Button>().Single(button => button.AutomationId == "creation-finalization-technical-details-toggle");
        ((IButtonController)toggle).SendClicked();
        Require(binding.IsVisible, "Technical detail toggle did not reveal the review binding.");
        Require(labels.Where(label => label.AutomationId?.StartsWith("creation-finalization-source-", StringComparison.Ordinal) == true)
            .All(label => label.IsVisible), "Technical detail toggle did not reveal source identities.");
        ((IButtonController)toggle).SendClicked();
        Require(!binding.IsVisible, "Technical detail toggle did not collapse the review binding.");
        Require(labels.Where(label => label.AutomationId?.StartsWith("creation-finalization-source-", StringComparison.Ordinal) == true)
            .All(label => !label.IsVisible), "Technical detail toggle did not collapse source identities.");
        refresh.Invoke(page, null);
        ((IButtonController)toggle).SendClicked();
        Require(!binding.IsVisible && !body.Children.OfType<Label>().Single(label => label.AutomationId == "creation-finalization-binding").IsVisible,
            "A detached review toggle disclosed stale authority.");
        Require(JsonSerializer.Serialize(review) == original && coordinator.IsCreationFinalizationReviewCurrent(review),
            "Display names or technical disclosure changed the admitted review authority.");
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
        public int LoadCalls { get; private set; }
        public bool RequireOffContextLoads { get; set; }
        public Action? AfterLoad { get; set; }
        public CharacterCreationFinalizationResult<CharacterCreationFinalizationState> Load(
            OwnerContextStamp owner, CharacterCreationFinalizationLoadRequest request)
        {
            Require(!RequireOffContextLoads || !ReferenceEquals(SynchronizationContext.Current, uiContext),
                "Persisted receipt Load ran synchronously on the caller's UI context.");
            LoadCalls++;
            var result = actual.Load(owner, request);
            AfterLoad?.Invoke();
            return result;
        }
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
        bool stopBeforeQualities = false, string buildMethod = CharacterCreationBuildMethods.Priority,
        bool stopBeforeAttributes = false, string fixtureAlias = "Finalizer",
        string? attributeTalent = null, string attributeTalentRank = "C", bool stopBeforeGear = false,
        bool stopBeforeResources = false, bool stopBeforeSkills = false, string metatypeName = "Human")
    {
        // Test fixture adapted from Core f750 CharacterCreationFinalizationServiceTests.ReadyContext:
        // canonical Priority or Sum-to-Ten; repeated ranks for Human, distinct ranks for Elf/Ork;
        // actual services issue every draft and receipt.
        // No manual auxiliary state, fake admission, finalization plan or receipt.
        var services = runtime.Services;
        var store = services.GetRequiredService<IWorkspaceStore>();
        var bootstrap = services.GetRequiredService<ICharacterCreationBootstrapService>();
        Require(CharacterCreationBootstrapProfiles.TryResolveCanonicalSettingsProfileId(
            buildMethod, out string profile), "Canonical build-method profile missing.");
        var created = bootstrap.Create(new(CharacterCreationBootstrapSchemas.RequestV1,
            CharacterCreationBootstrapStages.AwaitingFoundationSelection, "sr5", "Finalization Runner", fixtureAlias,
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
        if (buildMethod == CharacterCreationBuildMethods.SumToTen && metatypeName == "Human")
        {
            ranks[CharacterCreationPriorityCategoryIds.Heritage] = "E";
            ranks[CharacterCreationPriorityCategoryIds.Attributes] = "A";
            ranks[CharacterCreationPriorityCategoryIds.Skills] = "A";
            ranks[CharacterCreationPriorityCategoryIds.Resources] = "C";
        }
        if (attributeTalent is not null)
        {
            Require(stopBeforeAttributes, "Awakened fixture is only intended for attribute allocation tests.");
            ranks[CharacterCreationPriorityCategoryIds.Heritage] = "B";
            ranks[CharacterCreationPriorityCategoryIds.Talent] = attributeTalentRank;
            ranks[CharacterCreationPriorityCategoryIds.Attributes] = attributeTalentRank == "A" ? "C" : "A";
            ranks[CharacterCreationPriorityCategoryIds.Skills] = "D";
            ranks[CharacterCreationPriorityCategoryIds.Resources] = "E";
        }
        var heritage = initial.Authority.Options.Single(item => item.CategoryId == CharacterCreationPriorityCategoryIds.Heritage
                && item.Rank == ranks[CharacterCreationPriorityCategoryIds.Heritage]).HeritageOptions.First(item => item.IsEnabled
                && item.MetavariantSourceId is null && item.MetavariantName is null && item.MetatypeName == metatypeName);
        var talentOption = initial.Authority.Options.Single(item => item.CategoryId == CharacterCreationPriorityCategoryIds.Talent
                && item.Rank == ranks[CharacterCreationPriorityCategoryIds.Talent]).TalentOptions.First(item => item.IsEnabled
                && item.Value.Equals(attributeTalent ?? CharacterCreationMagicResonanceKinds.Mundane, StringComparison.OrdinalIgnoreCase)
                && (attributeTalent is not null || item.Magic is null && item.Resonance is null && item.Depth is null
                    && item.ActiveSkillGrant is null && item.SkillGroupGrant is null));
        var talentSkills = talentOption.ActiveSkillGrant?.Options.Where(item => item.IsEnabled)
            .Take(talentOption.ActiveSkillGrant.Quantity).Select(item => item.SelectionId).ToArray() ?? [];
        var talentGroups = talentOption.SkillGroupGrant?.Options
            .Take(talentOption.SkillGroupGrant.Quantity).Select(item => item.SelectionId).ToArray() ?? [];
        var prerequisitePreview = prerequisites.Preview(new(initial.Binding, ranks)
            { HeritageSelectionId = heritage.SelectionId, TalentSelectionId = talentOption.SelectionId,
                TalentActiveSkillSelectionIds = talentSkills, TalentSkillGroupSelectionIds = talentGroups }).Value!;
        var prerequisiteReceipt = prerequisites.Confirm(new(prerequisitePreview.Binding, ranks,
            prerequisitePreview.PreviewDigest, ExplicitlyConfirmed: true)
            { HeritageSelectionId = heritage.SelectionId, TalentSelectionId = talentOption.SelectionId,
                TalentActiveSkillSelectionIds = talentSkills, TalentSkillGroupSelectionIds = talentGroups });
        Require(prerequisiteReceipt.Outcome == CharacterCreationFoundationOutcomes.Success,
            "Actual prerequisite failed: " + JsonSerializer.Serialize(prerequisiteReceipt));
        if (stopBeforeAttributes)
            return new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!;
        var attributes = services.GetRequiredService<ICharacterCreationAttributesService>();
        var attributePreview = attributes.Preview(new(attributes.Load(new(runtime.Id)).Value!.Binding, [])).Value!;
        var attributeReceipt = attributes.Confirm(new(attributePreview.Binding, [], attributePreview.PreviewDigest, true));
        Require(attributeReceipt.Outcome == CharacterCreationFoundationOutcomes.Success,
            "Actual Attributes failed: " + JsonSerializer.Serialize(attributeReceipt));
        if (stopBeforeSkills)
            return new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!;
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
        if (stopBeforeResources)
            return new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!;
        var resources = services.GetRequiredService<ICharacterCreationResourcesService>();
        var resourcesState = resources.Load(new(runtime.Id)).Value!;
        var zero = resourcesState.Options.First(item => item.IsEnabled && item.KarmaInvestment == 0);
        var resourcePreview = resources.Preview(new(resourcesState.Binding, zero.OptionId)).Value!;
        var resourceReceipt = resources.Confirm(new(resourcePreview.Binding, zero.OptionId, resourcePreview.PreviewDigest,
            "native-finalization-resources", true));
        Require(resourceReceipt.Outcome == CharacterCreationResourcesOutcomes.Applied,
            "Actual Resources failed: " + JsonSerializer.Serialize(resourceReceipt));
        if (stopBeforeGear)
            return new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!;
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
