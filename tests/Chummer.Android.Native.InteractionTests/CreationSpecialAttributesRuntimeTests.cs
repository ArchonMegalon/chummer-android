using System.Globalization;
using Chummer.Android.Native;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Workspaces;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    internal static async Task RunSpecialAttributesAsync(string contentRoot, string? smokePath)
    {
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            foreach (string method in new[] { CharacterCreationBuildMethods.Priority, CharacterCreationBuildMethods.SumToTen })
            {
                var owners = new ControlledLinkedOwner();
                await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners,
                    creationFinalization: true, creationAttributes: true, productionCreationOverview: true);
                var before = PrepareActualFinalizationReadyContext(runtime, buildMethod: method, stopBeforeAttributes: true);
                await HydrateFinalizationOwnerAsync(runtime, owners, before);
                var coordinator = runtime.Coordinator;
                var state = coordinator.LoadCreationAttributes().Value!;
                Require(state.SpecialPointBudget.Remaining > 0 && state.Attributes.Single(x => x.AttributeId == "EDG").IsEnabled,
                    "SETUP: actual Priority Human must have spendable special points.");
                var page = new CreationAttributesPage(coordinator, state);
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing")));
                var scroll = (ScrollView)page.Content!;
                Element? target = null;
                ((IScrollViewController)scroll).ScrollToRequested += (_, request) =>
                {
                    target = request.Element;
                    ((IScrollViewController)scroll).SendScrollFinished();
                };
                var specialJump = MinimalVisible(page).OfType<Button>().SingleOrDefault(x =>
                    x.AutomationId == "creation-attributes-budget-special-jump");
                Require(specialJump is { IsEnabled: true }, "Special points have no clickable allocation entry.");
                ((IButtonController)specialJump!).SendClicked();
                Require(target?.AutomationId == "creation-attributes-special-heading", "Special points jumped to the wrong group.");
                var normalJump = MinimalVisible(page).OfType<Button>().Single(x =>
                    x.AutomationId == "creation-attributes-budget-normal-jump");
                ((IButtonController)normalJump).SendClicked();
                Require(target?.AutomationId == "creation-attributes-normal-heading", "Normal points lost their own destination.");
                target = null;
                MinimalRender(page);
                ((IButtonController)specialJump!).SendClicked();
                Require(target is null, "Detached special points entry still navigates.");
                var draft = new CreationAttributesPhoneDraft();
                draft.Bind(state, coordinator.State);
                var editor = new CreationAttributeAllocationPage(coordinator, draft, "EDG", state);
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(editor, "OnAppearing")));
                Button Plus() => MinimalVisible(editor).OfType<Button>().Single(x =>
                    x.AutomationId == "creation-attribute-priority-increase-edg");
                Button Minus() => MinimalVisible(editor).OfType<Button>().Single(x =>
                    x.AutomationId == "creation-attribute-priority-decrease-edg");
                var culture = CultureInfo.CurrentUICulture;
                try
                {
                    foreach (string locale in new[] { "en-GB", "de-AT", "es-MX" })
                    {
                        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(locale);
                        MinimalRender(editor);
                        Require(Plus().IsEnabled && Plus().Text == CreationAllocationStrings.Get("AttributeAllocation.SpecialIncrease", "missing")
                            && Minus().Text == CreationAllocationStrings.Get("AttributeAllocation.SpecialDecrease", "missing"),
                            "Special point controls are disabled or wrongly labelled in " + locale);
                    }
                }
                finally { CultureInfo.CurrentUICulture = culture; MinimalRender(editor); }
                int initialEdge = draft.Attribute(state, "EDG")!.Current;
                decimal remaining = draft.SpecialBudget(state).Remaining;
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((IButtonController)Plus()).SendClicked()));
                Require(draft.Attribute(state, "EDG")!.Current == initialEdge + 1
                    && draft.SpecialBudget(state).Remaining == remaining - 1
                    && draft.NormalBudget(state) == state.NormalPointBudget
                    && draft.KarmaBudget(state) == state.CreationKarmaBudget,
                    "Edge + did not spend exactly one special point independently of normal points and Karma.");
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((IButtonController)Minus()).SendClicked()));
                Require(draft.Attribute(state, "EDG")!.Current == initialEdge
                    && draft.SpecialBudget(state).Remaining == remaining, "Edge - did not return the special point.");
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((IButtonController)Plus()).SendClicked()));
                var allocations = draft.Allocations(state);
                RequireSameRewardDocument(before, new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!);
                var preview = coordinator.PreviewCreationAttributes(state.Binding, allocations).Value!;
                var applied = await coordinator.ConfirmCreationAttributesAsync(preview, allocations);
                Require(applied is { Outcome: CharacterCreationFoundationOutcomes.Success, Receipt: not null },
                    "Special point allocation could not be saved.");
                var saved = new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!;
                Require(saved.ContentRevision == before.ContentRevision + 1 && saved.SavedRevision == saved.ContentRevision,
                    "Special point confirmation must save exactly one revision.");
                await HydrateFinalizationOwnerAsync(runtime, owners, saved);
                var reopened = coordinator.LoadCreationAttributes().Value!;
                Require(reopened.Attributes.Single(x => x.AttributeId == "EDG").Current == initialEdge + 1
                    && reopened.SpecialPointBudget.Remaining == remaining - 1, "Special allocation lost on cold-store reopen.");
                // A fully spent pool or an attribute cap must still disable another +.
                var cappedDraft = new CreationAttributesPhoneDraft();
                cappedDraft.Bind(reopened, coordinator.State);
                var edge = cappedDraft.Attribute(reopened, "EDG")!;
                int legalExtra = Math.Min(edge.Maximum - edge.Current, (int)reopened.SpecialPointBudget.Remaining);
                if (legalExtra > 0)
                {
                    var capped = cappedDraft.ChangedAllocations(reopened, "EDG", legalExtra, 0)!;
                    Require(cappedDraft.TryAdopt(reopened, coordinator.State,
                        coordinator.PreviewCreationAttributes(reopened.Binding, capped), capped), "SETUP: Core cap preview rejected.");
                }
                var cappedPage = new CreationAttributeAllocationPage(coordinator, cappedDraft, "EDG", reopened);
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(cappedPage, "OnAppearing")));
                Require(!MinimalVisible(cappedPage).OfType<Button>().Single(x =>
                    x.AutomationId == "creation-attribute-priority-increase-edg").IsEnabled,
                    "Special points bypassed the Core budget or attribute cap.");
                RequireSameRewardDocument(saved, new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!);
                var originalOwner = owners.Current;
                owners.Set(ContactsOwnerB);
                owners.Set(originalOwner);
                MinimalRender(cappedPage);
                Require(!MinimalVisible(cappedPage).OfType<Button>().Any(x => x.IsEnabled), "Owner ABA revived special allocation.");
                IssuedPageLifecycle(cappedPage, "OnDisappearing");
                IssuedPageLifecycle(editor, "OnDisappearing");
                IssuedPageLifecycle(page, "OnDisappearing");
                if (method == CharacterCreationBuildMethods.Priority && smokePath is not null)
                {
                    Require(Path.IsPathFullyQualified(smokePath), "Use an explicit private smoke seed path.");
                    File.Copy(Path.Combine(runtime.StateDirectory, "workspaces", runtime.Id.Value + ".json"), smokePath, overwrite: false);
                }
                Console.WriteLine("PASS " + method + " special entry, EN/DE/ES controls, Edge +/-, separate budgets, save/cold reopen, cap and owner ABA");
            }
        });
    }
}
