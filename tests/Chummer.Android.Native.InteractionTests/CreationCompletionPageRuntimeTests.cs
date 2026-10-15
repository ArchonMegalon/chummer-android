using System.Globalization;
using System.Reflection;
using Chummer.Android.Native;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Chummer.Presentation.Creation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    internal static async Task RunCreationCompletionPageAsync(string contentRoot, string? scenarioFilter = null)
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
                foreach (string scenario in scenarioFilter is null
                    ? new[] { "prerequisite", "cash-read-throws", "busy-cancel", "cancel", "magic-choice-cancel", "loss-decline", "loss-owner-aba", "loss-departure", "apply", "lost-reply", "unknown", "optional-karma" }
                    : new[] { scenarioFilter })
                {
                    CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(scenario switch
                    {
                        "apply" => "de-AT",
                        "cancel" => "es-ES",
                        _ => "en-GB"
                    });
                    var owners = new ControlledLinkedOwner();
                    using var reviewPause = scenario == "busy-cancel" ? new CompletionReviewPause() : null;
                    CompletionServiceProbe? service = null;
                    ThrowingCompletionCashService? throwingCash = null;
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
                            if (scenario == "cash-read-throws") return throwingCash = new(service);
                            return reviewPause is null ? service : new PausedCompletionReviewService(service, reviewPause);
                        });
                    await runtime.Coordinator.InitializeAsync();
                    await AccountStartupTask(runtime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
                    var before = scenario == "prerequisite"
                        ? PreparePrerequisiteOwnerFixture(runtime, CharacterCreationBuildMethods.Priority)
                        : scenario == "magic-choice-cancel"
                            ? PrepareActualFinalizationReadyContext(runtime, stopBeforeAttributes: true,
                                attributeTalent: "Magician", attributeTalentRank: "B", completionMagic: true)
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
                    int lossConfirmations = 0;
                    using var alerts = new IssuedPageAlerts(page, window, confirmation =>
                    {
                        var reviewed = (CharacterCreationCompletionApplyReview?)typeof(CreationCompletionPage)
                            .GetField("_review", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)
                            ?? throw new InvalidOperationException("A loss confirmation has no actual Core review.");
                        Require(confirmation.Title == CreationCompletionPage.Copy("Loss.Title", "missing")
                            && confirmation.Accept == CreationCompletionPage.Copy("Loss.Continue", "missing")
                            && confirmation.Cancel == CreationCompletionPage.Copy("Loss.Back", "missing")
                            && confirmation.Message == ExpectedCompletionLossMessage(reviewed.Projection)
                            && service!.ApplyCalls == 0 && lossConfirmations == 0,
                            "Unexpected or repeated confirmation reached the completion handler.");
                        lossConfirmations++;
                        if (scenario == "loss-owner-aba")
                        {
                            var originalOwner = owners.Capture();
                            owners.Set(ContactsOwnerB);
                            owners.Set(originalOwner.Owner);
                            Require(owners.Capture() != originalOwner,
                                "SETUP: the real owner stamp must change through A→B→A.");
                        }
                        else if (scenario == "loss-departure") IssuedPageLifecycle(page, "OnDisappearing");
                        confirmation.SetResult(scenario != "loss-decline"); // Explicit synthetic user choice only.
                    });
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
                    if (scenario == "magic-choice-cancel")
                    {
                        var choices = CompletionRecommendationBuilder.GetMagicIdentityChoices(
                            inputs, CharacterCreationMagicResonanceKinds.Tradition).ToArray();
                        Picker tradition = CompletionPageControl<Picker>(page, "creation-completion-tradition");
                        Require(inputs.Magic.SelectedTalent.Kind == CharacterCreationMagicResonanceKinds.Magician
                            && inputs.Magic.Selections.Tradition is null && inputs.Magic.Selections.Spells.Count == 0
                            && choices.Length > 1 && tradition.SelectedIndex == 0
                            && (string)tradition.ItemsSource[0]! == MagicCatalogStrings.OptionName(
                                choices[0].Identity.Kind, choices[0].Identity.SourceId, choices[0].Name),
                            "SETUP: a genuine unfinished Magician must show its initial admitted tradition before Preview.");
                        int shamanic = Array.FindIndex(choices,
                            row => row.Identity.SourceId == "8d185e0e-5f49-4992-babd-d1ac9c848f68");
                        Require(shamanic > 0, "SETUP: the actual Shamanic identity must be an editable non-default choice.");
                        tradition.SelectedIndex = shamanic;
                    }
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
                    CharacterCreationStartingNuyenSource? originalCashSource = null;
                    if (scenario == "cash-read-throws")
                    {
                        var originalSources = (CharacterCreationStartingNuyenSource[])typeof(CreationCompletionPage)
                            .GetField("_cashSources", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
                        string? originalDigest = (string?)typeof(CreationCompletionPage)
                            .GetField("_cashDigest", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page);
                        originalCashSource = originalSources.SingleOrDefault(row => row.AuthorityDigest == originalDigest);
                        Require(originalCashSource is not null && throwingCash!.CashReads == 0,
                            "SETUP: the page must retain real original cash terms before its proposed-home read fails.");
                    }
                    Button preview = CompletionPageControl<Button>(page, "creation-completion-preview");
                    await ClickCompletionControlAsync(ui, preview);
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
                    if (scenario == "cash-read-throws")
                    {
                        var retainedSources = (CharacterCreationStartingNuyenSource[])typeof(CreationCompletionPage)
                            .GetField("_cashSources", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
                        var retainedDigest = typeof(CreationCompletionPage)
                            .GetField("_cashDigest", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page);
                        Require(throwingCash!.CashReads == 1 && retainedSources.Length == 0 && retainedDigest is null
                            && !MinimalVisible(page).Any(element => element.AutomationId is
                                "creation-completion-cash-roll" or "creation-completion-cash-source"
                                or "creation-completion-review" or "creation-completion-apply")
                            && !MinimalVisible(page).OfType<Label>().Any(label => label.Text ==
                                CreationKarmaCopy.StartingCash(originalCashSource!.Name,
                                    originalCashSource.Dice, originalCashSource.Multiplier))
                            && MinimalVisible(page).OfType<Label>().Any(label => label.Text ==
                                CreationCompletionPage.Copy("CashUnavailable", "missing-cash-unavailable"))
                            && service!.ReviewCalls == 0 && service.ApplyCalls == 0,
                            "A failed proposal cash read retained stale original cash terms or exposed Review/Apply.");
                        MinimalRequireNoMachineValues(page);
                        AssertCompletionFilesUnchanged(runtime, files);
                        await ClickCompletionControlAsync(ui, CompletionPageControl<Button>(page, "creation-completion-cancel"));
                        ((IButtonController)preview).SendClicked();
                        await ui.DrainDispatchedAsyncVoidAsync();
                        Require(navigation.NavigationStack.Count == 1
                            && ReferenceEquals(Shell.Current?.CurrentPage, root)
                            && coordinator.State.Profile?.Created == false
                            && throwingCash.CashReads == 1 && service.ReviewCalls == 0 && service.ApplyCalls == 0
                            && owners.ActiveLeases == 0 && alerts.Titles.Count == 0,
                            "Cancel after a failed cash read must leave creation unchanged without replay or leaked authority.");
                        AssertCompletionFilesUnchanged(runtime, files);
                        IssuedPageLifecycle(page, "OnDisappearing");
                        ui.AssertHealthy();
                        Console.WriteLine("PASS actual Core/MAUI completion page: cash-read-throws");
                        continue;
                    }
                    if (scenario == "magic-choice-cancel")
                    {
                        var plan = proposal.Evaluation.Value
                            ?? throw new InvalidOperationException("The Magician preview has no actual Core plan.");
                        var magic = plan.Selections.MagicSelections;
                        var choices = CompletionRecommendationBuilder.GetMagicIdentityChoices(
                            inputs, CharacterCreationMagicResonanceKinds.Tradition).ToArray();
                        var shamanic = choices.Single(row => row.Identity.SourceId == "8d185e0e-5f49-4992-babd-d1ac9c848f68");
                        var expectedSpells = magic.Spells.Where(identity => !inputs.Magic.Selections.Spells.Contains(identity))
                            .Select(identity => inputs.Magic.Authority.Spells.Single(row => row.Identity == identity))
                            .Select(row => CreationCompletionPage.Copy("MagicSpell", "missing-spell") + ": "
                                + MagicCatalogStrings.OptionName(row.Identity.Kind, row.Identity.SourceId, row.Name))
                            .OrderBy(value => value, StringComparer.Ordinal).ToArray();
                        Require(proposal.Intent.PreferredTradition == shamanic.Identity
                            && magic.Tradition == shamanic.Identity && plan.Magic is { IsMagicComplete: true }
                            && expectedSpells.Length > 0
                            && MinimalVisible(page).OfType<Label>()
                                .Where(row => row.AutomationId == "creation-completion-proposed-spell")
                                .Select(row => row.Text).OrderBy(value => value, StringComparer.Ordinal).SequenceEqual(expectedSpells)
                            && CompletionPageControl<Label>(page, "creation-completion-proposed-tradition").Text
                                == CreationCompletionPage.Copy("MagicTradition", "missing-tradition") + ": "
                                    + MagicCatalogStrings.OptionName(shamanic.Identity.Kind, shamanic.Identity.SourceId, shamanic.Name),
                            "The actual page must preserve the edited tradition across refreshes and show exactly Core's new named spells.");
                        MinimalRequireNoMachineValues(page);
                        AssertCompletionFilesUnchanged(runtime, files);

                        var cashSources = (CharacterCreationStartingNuyenSource[])typeof(CreationCompletionPage)
                            .GetField("_cashSources", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
                        var cashDigest = (string?)typeof(CreationCompletionPage)
                            .GetField("_cashDigest", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page);
                        var magicCashSource = cashSources.Single(row => row.AuthorityDigest == cashDigest);
                        CompletionPageControl<Entry>(page, "creation-completion-cash-roll").Text =
                            (magicCashSource.Dice * 3).ToString(CultureInfo.InvariantCulture);
                        Button retiredReview = CompletionPageControl<Button>(page, "creation-completion-review");
                        Require(retiredReview.IsEnabled, "SETUP: explicit legal cash must make the old proposal reviewable.");
                        Picker change = CompletionPageControl<Picker>(page, "creation-completion-tradition");
                        int hermetic = Array.FindIndex(choices,
                            row => row.Identity.SourceId == "19320625-bc1a-492f-8904-da6a847e5700");
                        Require(hermetic >= 0 && change.SelectedIndex != hermetic,
                            "SETUP: the actual Hermetic identity must differ from the reviewed Shamanic suggestion.");
                        change.SelectedIndex = hermetic;
                        Require(typeof(CreationCompletionPage).GetField("_proposal", BindingFlags.Instance | BindingFlags.NonPublic)!
                                .GetValue(page) is null
                            && typeof(CreationCompletionPage).GetField("_review", BindingFlags.Instance | BindingFlags.NonPublic)!
                                .GetValue(page) is null
                            && (string?)typeof(CreationCompletionPage).GetField("_roll", BindingFlags.Instance | BindingFlags.NonPublic)!
                                .GetValue(page) == string.Empty
                            && !MinimalVisible(page).Any(row => row.AutomationId is
                                "creation-completion-review" or "creation-completion-apply" or "creation-completion-cash-roll"),
                            "Changing the visible tradition must retire the old proposal, review controls and cash input.");
                        ((IButtonController)retiredReview).SendClicked();
                        await ui.DrainDispatchedAsyncVoidAsync();
                        Require(service!.ReviewCalls == 0 && service.ApplyCalls == 0,
                            "A stale review button dispatched after the tradition changed.");
                        await ClickCompletionControlAsync(ui, CompletionPageControl<Button>(page, "creation-completion-preview"));
                        var rebuilt = (CompletionAssistantProposal?)typeof(CreationCompletionPage)
                            .GetField("_proposal", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page);
                        Require(rebuilt is { } rebuiltProposal && rebuiltProposal.Intent.PreferredTradition == choices[hermetic].Identity
                            && rebuiltProposal.Evaluation.Value?.Selections.MagicSelections.Tradition == choices[hermetic].Identity
                            && rebuiltProposal.ProposalDigest != proposal.ProposalDigest
                            && CompletionPageControl<Picker>(page, "creation-completion-tradition").SelectedIndex == hermetic,
                            "Rebuilding must use and visibly retain the newly selected exact tradition.");
                        await ClickCompletionControlAsync(ui, CompletionPageControl<Button>(page, "creation-completion-cancel"));
                        Require(navigation.NavigationStack.Count == 1 && ReferenceEquals(Shell.Current?.CurrentPage, root)
                            && coordinator.State.Profile?.Created == false && service.ReviewCalls == 0 && service.ApplyCalls == 0
                            && owners.ActiveLeases == 0 && alerts.Titles.Count == 0,
                            "Cancel after editing magic must return to unchanged creation without review, Apply or leaked authority.");
                        AssertCompletionFilesUnchanged(runtime, files);
                        IssuedPageLifecycle(page, "OnDisappearing");
                        ui.AssertHealthy();
                        Console.WriteLine("PASS actual Core/MAUI completion page: magic-choice-cancel");
                        continue;
                    }
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
                    var proposedCash = await coordinator.LoadCreationCompletionStartingCashAsync(proposal);
                    Require(proposedCash is { Outcome: CharacterCreationFoundationOutcomes.Success,
                            Value: not null, Blockers.Count: 0 },
                        "The admitted proposal did not resolve its actual starting-cash authority: "
                        + string.Join(", ", proposedCash.Blockers));
                    var cashSource = proposedCash.Value!;
                    var pageCashSources = (CharacterCreationStartingNuyenSource[])typeof(CreationCompletionPage)
                        .GetField("_cashSources", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
                    string? pageCashDigest = (string?)typeof(CreationCompletionPage)
                        .GetField("_cashDigest", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page);
                    Require(pageCashDigest == cashSource.AuthorityDigest
                        && pageCashSources.Count(row => row.AuthorityDigest == pageCashDigest) == 1
                        && CharacterCreationFinalizationDigest.Compute(pageCashSources.Single(row =>
                            row.AuthorityDigest == pageCashDigest)) == CharacterCreationFinalizationDigest.Compute(cashSource)
                        && MinimalVisible(page).OfType<Label>().Any(label => label.Text ==
                            CreationKarmaCopy.StartingCash(cashSource.Name, cashSource.Dice, cashSource.Multiplier)),
                        "The page's selected and displayed cash terms must match the exact admitted proposal source.");
                    if (scenario == "apply") AssertCompletionRichProposal(page, inputs, proposal, cashSource);
                    if (scenario == "optional-karma")
                    {
                        AssertOptionalAttributeKarmaDisclosure(page, proposal);
                        await AssertOptionalAttributeKarmaRejectionsAsync(page, runtime, owners, ui, inputs, proposal, cashSource);
                    }
                    AssertCompletionFilesUnchanged(runtime, files);
                    cash.Text = (cashSource.Dice * 3).ToString(CultureInfo.InvariantCulture);
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
                    if (scenario == "optional-karma") CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-AT");
                    await ClickCompletionControlAsync(ui, check);
                    Button apply = CompletionPageControl<Button>(page, "creation-completion-apply");
                    Require(apply.IsEnabled && service!.ReviewCalls == 1 && service.ApplyCalls == 0,
                        "The explicit final review was not produced read-only by Core.");
                    Require(service!.LastReviewProposal is { Selections.Loadout: { } reviewedLoadout } request
                        && request.InputsSnapshotDigest == inputs.SnapshotDigest
                        && CharacterCreationFinalizationDigest.Compute(reviewedLoadout)
                            == CharacterCreationFinalizationDigest.Compute(proposal.Loadout!.Selections),
                        "The actual Review handler did not send the exact typed loadout shown in the summary to Core.");
                    AssertCompletionHasNoAllocationSettings(page);
                    MinimalRequireNoMachineValues(page);
                    AssertCompletionFilesUnchanged(runtime, files);
                    Require(!MinimalVisible(page).Any(element => element.AutomationId == "creation-completion-read-busy"),
                        "Read-only progress remained visible after review completed.");
                    var reviewed = (CharacterCreationCompletionApplyReview)typeof(CreationCompletionPage)
                        .GetField("_review", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
                    AssertCompletionLossDisclosure(page, reviewed.Projection);
                    if (scenario == "optional-karma") AssertOptionalAttributeKarmaDisclosure(page, proposal);
                    int expectedLossConfirmations = CreationCompletionPage.LossItems(reviewed.Projection).Count > 0 ? 1 : 0;

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
                    else if (scenario.StartsWith("loss-", StringComparison.Ordinal))
                    {
                        Require(expectedLossConfirmations == 1, "SETUP: loss guards need a genuinely lossy Core projection.");
                        await ClickCompletionControlAsync(ui, apply);
                        Require(lossConfirmations == 1 && service!.ApplyCalls == 0
                            && coordinator.State.Profile?.Created == false
                            && (bool)typeof(CreationCompletionPage).GetField("_applyStarted",
                                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)! == false,
                            "Declined, departed or owner-stale loss acknowledgement dispatched a save.");
                        AssertCompletionFilesUnchanged(runtime, files);
                        if (scenario != "loss-decline")
                        {
                            ((IButtonController)apply).SendClicked();
                            await ui.DrainDispatchedAsyncVoidAsync();
                            Require(lossConfirmations == 1 && service.ApplyCalls == 0,
                                "A retained stale Apply control reopened confirmation or dispatched completion.");
                        }
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
                                && lossConfirmations == expectedLossConfirmations
                                && ReferenceEquals(navigation.NavigationStack.Last(), page),
                                "Unknown-outcome recovery repeated Apply/acknowledgement or claimed a completed Career transition.");
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
                    Require(owners.ActiveLeases == 0 && alerts.Titles.Count == lossConfirmations
                        && lossConfirmations == (scenario == "cancel" ? 0 : expectedLossConfirmations),
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

    private static void AssertOptionalAttributeKarmaDisclosure(CreationCompletionPage page, CompletionAssistantProposal proposal)
    {
        var plan = proposal.Evaluation.Value!;
        Require(proposal.Request.AllocateRemainingAttributeKarma
            && proposal.Evaluation.Outcome == CharacterCreationFoundationOutcomes.Blocked
            && plan.Review.Blockers.Count == 0
            && plan.Blockers.Contains(CharacterCreationAttributeCompletionBlockers.KarmaTargetsUnfulfilled),
            "SETUP: this scenario needs an actual blocked plan with a legal complete review and unmet Karma goals.");
        var expected = proposal.Request.Attributes.KarmaTargets!
            .Select(goal => (Goal: goal, Actual: plan.Review.Skills!.Attributes.Attributes
                .Single(row => row.AttributeId == goal.AttributeId).UnaugmentedCurrent))
            .Where(row => row.Actual < row.Goal.TargetUnaugmentedRating).ToArray();
        Require(expected.Length > 0 && plan.Selections.AttributeAllocations.Any(row => row.KarmaLevels > 0),
            "SETUP: the real proposal must retain an affordable Karma increase and leave optional goals unmet.");
        Require(!CreationCompletionPage.RequiredProposalBlockers(proposal, plan.Blockers)
                .Contains(CharacterCreationAttributeCompletionBlockers.KarmaTargetsUnfulfilled),
            "A legal optional-goal shortfall still produces generic failure copy.");
        var rows = MinimalVisible(page).OfType<Label>().Where(label => label.AutomationId?.StartsWith(
            "creation-completion-optional-karma-", StringComparison.Ordinal) == true).ToArray();
        Require(rows.Length == expected.Length, "Disclosure included met goals or omitted an actual optional shortfall.");
        foreach (var row in expected)
        {
            string text = CreationFlowStrings.Format("Completion.OptionalKarmaRating", "missing",
                CreationAllocationStrings.AttributeName(row.Goal.AttributeId), row.Actual, row.Goal.TargetUnaugmentedRating);
            Require(CompletionPageControl<Label>(page, "creation-completion-optional-karma-" + row.Goal.AttributeId).Text == text,
                "The page must show the accepted complete review's proposed rating and the actual requested goal.");
        }
        Require(CompletionPageControl<Label>(page, "creation-completion-optional-karma").Text
                == CreationCompletionPage.Copy("OptionalKarmaFunding", "missing"),
            "The funded baseline must explain preserved choices/reserves without claiming the missing goals were applied.");
        foreach (string key in new[] { "Completion.OptionalKarmaFunding", "Completion.OptionalKarmaGoals", "Completion.OptionalKarmaRating" })
        {
            string english = CreationFlowStrings.Get(key, "missing", CultureInfo.GetCultureInfo("en-GB"));
            Require(english != "missing"
                && CreationFlowStrings.Get(key, "missing", CultureInfo.GetCultureInfo("de-AT")) is var german
                && german != "missing" && german != english
                && CreationFlowStrings.Get(key, "missing", CultureInfo.GetCultureInfo("es-MX")) == english,
                "Optional-goal copy must use English/German resources and the existing English fallback for Spanish.");
        }
        MinimalRequireNoMachineValues(page);
    }

    private static async Task AssertOptionalAttributeKarmaRejectionsAsync(CreationCompletionPage page,
        NativeRewardRuntime runtime, ControlledLinkedOwner owners, IssuedPageUiContext ui,
        CharacterCreationCompletionReviewInputs inputs, CompletionAssistantProposal proposal,
        CharacterCreationStartingNuyenSource cashSource)
    {
        var plan = proposal.Evaluation.Value!;
        string[] optionalFindings = [CharacterCreationAttributeCompletionBlockers.KarmaTargetsUnfulfilled,
            CharacterCreationFinalizationBlockers.GlobalKarmaExceeded];
        var selections = plan.Selections;
        CharacterCreationCompletionSelections[] invalidChoices =
        [
            selections with { AttributeAllocations = selections.AttributeAllocations.Select(row =>
                row.AttributeId == "BOD" ? row with { KarmaLevels = 100 } : row).ToArray() },
            selections with { MagicSelections = selections.MagicSelections with { MysticAdeptPowerPoints = int.MaxValue } }
        ];
        var cash = new CharacterCreationStartingCashChoice(cashSource.AuthorityDigest, cashSource.Dice * 3);
        var actual = runtime.Services.GetRequiredService<IOwnerBoundCharacterCreationCompletionService>();
        for (int index = 0; index < invalidChoices.Length; index++)
        {
            var request = new CharacterCreationCompletionReviewRequest(inputs.SnapshotDigest, invalidChoices[index]);
            var rejected = await Task.Run(() => CharacterCreationCompletionReviewService.Review(inputs, request));
            Require(rejected.Outcome != CharacterCreationFoundationOutcomes.Success && rejected.Blockers.Count > 0,
                "Actual Core accepted an illegal or unpriceable selection.");
            if (index == 1)
                Require(rejected.Value?.Balances.RemainingCreationKarma is null,
                    "Unpriceable selections must not turn into a known zero-cost budget.");
            var invalid = proposal with { Evaluation = proposal.Evaluation with { Value = rejected.Value is null ? null : plan with
            {
                Selections = invalidChoices[index],
                Review = rejected.Value
            } } };
            Require(CreationCompletionPage.OptionalAttributeKarmaShortfalls(invalid).Count == 0
                && CreationCompletionPage.RequiredProposalBlockers(invalid, optionalFindings).SequenceEqual(optionalFindings),
                "A rejected complete review was relabeled as optional because of a blocker name.");
            var refused = await Task.Run(() => actual.Review(owners.Capture(), runtime.Id, request, cash));
            Require(refused.Outcome != CharacterCreationFoundationOutcomes.Success && refused.Value is null,
                "Real owner-bound Core review admitted invalid choices for Apply.");
            var nativeRefusal = await runtime.Coordinator.ReviewCreationCompletionAsync(invalid, cash);
            Require(nativeRefusal.Value is null && nativeRefusal.Outcome != CharacterCreationFoundationOutcomes.Success,
                "The native gate admitted a substituted invalid proposal.");
        }

        // Same accepted Core review, different actual reserve request: a reserve
        // name alone cannot tell whether a failed candidate or retained plan broke it.
        foreach (var request in new[]
        {
            proposal.Request with { KarmaToReserve = plan.Review.Balances.RemainingCreationKarma!.Value + 1 },
            proposal.Request with { NuyenToReserve = plan.Review.Balances.RemainingNuyen!.Value + 1m },
            proposal.Request with { Attributes = proposal.Request.Attributes with
                { NormalPointsToReserve = (int)plan.Review.Skills!.Attributes.NormalPointBudget.Remaining + 1 } },
            proposal.Request with { AllocateRemainingAttributeKarma = false },
            proposal.Request with { Attributes = proposal.Request.Attributes with { KarmaTargets = [] } }
        })
        {
            var differentRequest = proposal with { Request = request };
            Require(CreationCompletionPage.OptionalAttributeKarmaShortfalls(differentRequest).Count == 0
                && CreationCompletionPage.RequiredProposalBlockers(differentRequest, optionalFindings).SequenceEqual(optionalFindings),
                "Violated requested reserves or absent optional goals were described as preserved optional advice.");
        }
        await ui.DrainDispatchedAsyncVoidAsync();
        Require(!MinimalVisible(page).Any(element => element.AutomationId == "creation-completion-apply"),
            "Negative review probes exposed Apply before the user's actual Core review.");
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

    private static void AssertCompletionRichProposal(CreationCompletionPage page,
        CharacterCreationCompletionReviewInputs inputs, CompletionAssistantProposal proposal,
        CharacterCreationStartingNuyenSource cashSource)
    {
        var plan = proposal.Evaluation.Value
            ?? throw new InvalidOperationException("The rich proposal has no actual Core evaluation.");
        var purchases = plan.Review.Lifestyles
            ?? throw new InvalidOperationException("The rich proposal has no evaluated purchases.");
        Require(inputs.Lifestyles.Lifestyles.Count == 0 && purchases.StartingLifestyleId is not null,
            "SETUP: the positive page route must propose a starting home that has not been saved.");
        var home = purchases.Lifestyles.Single(row =>
            row.Configuration.LifestyleId == purchases.StartingLifestyleId);
        var homeOption = purchases.Authority.LifestyleOptions.Single(row =>
            row.OptionId == home.Configuration.BaseLifestyleOptionId);
        Require(cashSource.SourceId == homeOption.SourceId.ToString("D")
            && CharacterCreationFinalizationStartingCashRules.SourcesMatch(cashSource, purchases.Authority),
            "Starting cash came from the old fallback rather than the proposed home's exact source authority.");

        var contacts = plan.Selections.Contacts.Where(row =>
            !inputs.Contacts.Contacts.Any(original => original.ContactId == row.ContactId)).ToArray();
        var homes = purchases.Lifestyles.Where(row =>
            !inputs.Lifestyles.Lifestyles.Any(original => original.LifestyleId == row.Configuration.LifestyleId)).ToArray();
        var equipment = purchases.Gear.Lines.Where(row =>
            inputs.Lifestyles.Gear.State.PendingDraft?.Lines.Any(original => original.OptionId == row.OptionId) != true).ToArray();
        Require(contacts.Length > 0 && homes.Length > 0 && equipment.Length > 0
            && homes.All(row => row.Economics.TotalCost > 0) && equipment.All(row => row.TotalCost > 0),
            "SETUP: the real rich proposal must include contacts, a priced home and priced equipment.");
        Require(VisibleRows("creation-completion-proposed-contact").SequenceEqual(contacts.Select(row =>
                    CreationFlowStrings.Format("Completion.ContactAddition", "{0} · Connection {1}, Loyalty {2}",
                        row.Identity.Name, row.Connection, row.Loyalty)).OrderBy(value => value, StringComparer.Ordinal))
            && VisibleRows("creation-completion-proposed-lifestyle").SequenceEqual(homes.Select(row =>
                    row.BaseLifestyleName + ": " + row.Economics.TotalCost.ToString("0.##", CultureInfo.CurrentCulture) + " ¥")
                .OrderBy(value => value, StringComparer.Ordinal))
            && VisibleRows("creation-completion-proposed-equipment").SequenceEqual(equipment.Select(row =>
                    row.Name + " × " + row.Quantity.ToString(CultureInfo.CurrentCulture) + ": "
                    + row.TotalCost.ToString("0.##", CultureInfo.CurrentCulture) + " ¥")
                .OrderBy(value => value, StringComparer.Ordinal)),
            "Visible proposed contacts, home and equipment must show the actual selections, quantities and Core-evaluated costs.");

        var loadoutInputs = inputs.Loadout
            ?? throw new InvalidOperationException("The actual page did not opt into Core's owner-bound loadout reader.");
        var loadout = proposal.Loadout
            ?? throw new InvalidOperationException("The actual proposal has no Core-priced typed loadout summary.");
        Require(loadout.Blockers.Count == 0
            && loadout.Lines.Any(row => row.Kind == CharacterCreationLoadoutKinds.Weapon)
            && loadout.Lines.Any(row => row.Kind == CharacterCreationLoadoutKinds.Armor)
            && loadout.Lines.All(row => !string.IsNullOrWhiteSpace(row.Name) && row.Quantity > 0 && row.TotalCost > 0)
            && CharacterCreationFinalizationDigest.Compute(plan.Selections.Loadout!)
                == CharacterCreationFinalizationDigest.Compute(loadout.Selections)
            && loadoutInputs.OriginalSelections.Weapons.All(loadout.Selections.Weapons.Contains)
            && loadoutInputs.OriginalSelections.Armor.All(loadout.Selections.Armor.Contains),
            "The actual proposal must retain manual choices and summarize the same typed weapon/armor selections Core evaluated.");
        Require(VisibleRows("creation-completion-proposed-loadout").SequenceEqual(loadout.Lines.Select(row =>
                    CreationFlowStrings.Format("Completion.LoadoutLine", "{0} × {1}: {2} ¥",
                        row.Name, row.Quantity.ToString(CultureInfo.CurrentCulture),
                        row.TotalCost.ToString("0.##", CultureInfo.CurrentCulture)))
                .OrderBy(value => value, StringComparer.Ordinal))
            && proposal.RemainingTasks.Contains("completion.remaining.ammo-binding-loading-modifications")
            && proposal.RemainingTasks.Contains("completion.remaining.commlinks-rated-gear-child-graphs")
            && MinimalVisible(page).OfType<Label>().Any(label => label.Text == CreationCompletionPage.Copy("Loadout", "missing-loadout"))
            && MinimalVisible(page).OfType<Label>().Any(label => label.Text == CreationCompletionPage.Copy("AmmoExtras", "missing-ammo-extras"))
            && MinimalVisible(page).OfType<Label>().Any(label => label.Text == CreationCompletionPage.Copy("AdvancedGear", "missing-advanced-gear")),
            "The summary must show Core's names, quantities and costs while distinguishing loose ammo from unsupported loading, modifications and advanced gear.");

        var previousCulture = CultureInfo.CurrentUICulture;
        try
        {
            foreach (string locale in new[] { "en-GB", "de-AT", "es-ES" })
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(locale);
                foreach (string key in new[] { "AmmoExtras", "AdvancedGear" })
                    Require(CreationCompletionPage.Copy(key, "missing-coverage") != "missing-coverage",
                        "Completion equipment limitations must remain readable in every shipping language.");
            }
        }
        finally { CultureInfo.CurrentUICulture = previousCulture; }

        IEnumerable<string?> VisibleRows(string id) => MinimalVisible(page).OfType<Label>()
            .Where(row => row.AutomationId == id).Select(row => row.Text)
            .OrderBy(value => value, StringComparer.Ordinal);
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

    private sealed class ThrowingCompletionCashService(IOwnerBoundCharacterCreationCompletionService actual)
        : IOwnerBoundCharacterCreationCompletionService
    {
        public int CashReads { get; private set; }

        public CharacterCreationFoundationResult<CharacterCreationStartingNuyenSource> LoadStartingCash(
            OwnerContextStamp owner, CharacterWorkspaceId workspaceId, CharacterCreationCompletionReviewRequest proposal,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CashReads++;
            throw new IOException("Synthetic proposal cash-read failure.");
        }

        public CharacterCreationFoundationResult<CharacterCreationCompletionApplyReview> Review(
            OwnerContextStamp owner, CharacterWorkspaceId workspaceId, CharacterCreationCompletionReviewRequest proposal,
            CharacterCreationStartingCashChoice startingCash, CancellationToken cancellationToken = default)
            => actual.Review(owner, workspaceId, proposal, startingCash, cancellationToken);

        public CharacterCreationFoundationResult<CharacterCreationFinalizationReceipt> Apply(
            OwnerContextStamp owner, CharacterCreationCompletionApplyRequest request,
            CancellationToken cancellationToken = default)
            => actual.Apply(owner, request, cancellationToken);
    }

    private sealed class PausedCompletionReviewService(IOwnerBoundCharacterCreationCompletionService actual,
        CompletionReviewPause pause) : IOwnerBoundCharacterCreationCompletionService
    {
        public CharacterCreationFoundationResult<CharacterCreationStartingNuyenSource> LoadStartingCash(
            OwnerContextStamp owner, CharacterWorkspaceId workspaceId, CharacterCreationCompletionReviewRequest proposal,
            CancellationToken cancellationToken = default)
            => actual.LoadStartingCash(owner, workspaceId, proposal, cancellationToken);

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
