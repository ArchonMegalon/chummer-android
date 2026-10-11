using System.Reflection;
using System.Text.Json;
using Chummer.Android.Native;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Workspaces;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    internal static async Task RunCreationDraftSlidersAsync(string contentRoot)
    {
        using (var inputUi = new IssuedPageUiContext())
            await inputUi.RunAsync(async () =>
            {
                int current = 0;
                int changes = 0;
                CreationIntegerSlider? slider = null;
                slider = new("integer-slider-test", "Rating", () => current, () => (0, 6, true), value =>
                {
                    changes++;
                    if (value <= 2) current = value;
                    slider!.UpdateProjection();
                    return Task.CompletedTask;
                });
                await JoinIssuedPageAsync(inputUi.BeginAsyncVoid(() => slider.Value = 1.2));
                await inputUi.DrainDispatchedAsyncVoidAsync();
                Require(current == 1 && slider.Value == 1 && changes == 1,
                    $"Fractional snapping replayed a local edit: current={current}, slider={slider.Value}, changes={changes}.");
                await JoinIssuedPageAsync(inputUi.BeginAsyncVoid(() => slider.Value = 3));
                await inputUi.DrainDispatchedAsyncVoidAsync();
                Require(current == 1 && slider.Value == 1 && changes == 2,
                    "Refused slider value replayed instead of returning to the accepted integer.");
                Console.WriteLine("PASS integer slider snapping and refusal: one edit per input, no replay");
            });
        await VerifySkillRatingCacheAsync(contentRoot);
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            foreach (string method in new[] { CharacterCreationBuildMethods.Priority, CharacterCreationBuildMethods.SumToTen })
            {
                Console.WriteLine($"SETUP {method} attribute slider fixture");
                var owners = new ControlledLinkedOwner();
                AttributesCommitProbe? probe = null;
                await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners,
                    creationFinalization: true, creationAttributes: true, productionCreationOverview: true,
                    attributesDecorator: actual => probe = new(actual));
                var before = PrepareActualFinalizationReadyContext(runtime, stopBeforeAttributes: true,
                    buildMethod: method, attributeTalent: "Mystic Adept", attributeTalentRank: "C");
                await HydrateFinalizationOwnerAsync(runtime, owners, before);
                Console.WriteLine($"SETUP {method} attribute fixture hydrated");
                var coordinator = runtime.Coordinator;
                var state = coordinator.LoadCreationAttributes().Value!;
                var page = new CreationAttributesPage(coordinator, state);
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing")));
                var draft = (CreationAttributesPhoneDraft)typeof(CreationAttributesPage)
                    .GetField("_draft", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
                Slider SliderFor(string id) => MinimalVisible(page).OfType<Slider>().Single(x =>
                    x.AutomationId == "creation-attributes-slider-" + id.ToLowerInvariant());
                var body = SliderFor("BOD");
                var magic = SliderFor("MAG");
                int reads = probe!.LoadCalls + probe.PreviewCalls;
                var timing = System.Diagnostics.Stopwatch.StartNew();
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => body.Value = 2.4));
                await ui.DrainDispatchedAsyncVoidAsync();
                Require(body.Value == 2 && draft.Attribute(state, "BOD")!.Current == 2,
                    "Attribute slider did not snap to the accepted integer.");
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => body.Value = 3));
                await ui.DrainDispatchedAsyncVoidAsync();
                var plus = MinimalVisible(page).OfType<Button>().Single(x =>
                    x.AutomationId == "creation-attributes-inline-increase-bod");
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((IButtonController)plus).SendClicked()));
                Require(body.Value == 4 && draft.NormalBudget(state).Remaining == state.NormalPointBudget.Remaining - 3,
                    "Slider, + and remaining normal points disagree.");
                int startingMagic = draft.Attribute(state, "MAG")!.Current;
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => magic.Value = startingMagic + 1));
                await ui.DrainDispatchedAsyncVoidAsync();
                Require(magic.Value == startingMagic + 1
                    && draft.SpecialBudget(state).Remaining == state.SpecialPointBudget.Remaining - 1,
                    "Magic slider did not consume the separate special pool.");
                Require(probe.LoadCalls + probe.PreviewCalls == reads && probe.ConfirmCalls == 0,
                    "Attribute editing performed store/source I/O or an implicit save.");
                RequireSameRewardDocument(before, new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!);
                Console.WriteLine($"PASS {method} normal/special integer sliders + button: {timing.ElapsedMilliseconds}ms, zero Core I/O");

                var allocations = draft.Allocations(state);
                var local = draft.PreviewPrepared(state, allocations).Value!;
                Require(!coordinator.CanOfferCreationAttributesConfirmation(local, allocations),
                    "A local cached projection acquired confirmation authority.");
                var fresh = coordinator.PreviewCreationAttributes(state.Binding, allocations).Value!;
                Require(probe.PreviewCalls > 0 && coordinator.CanOfferCreationAttributesConfirmation(fresh, allocations),
                    "Review did not retain a fresh Core preview.");
                var confirmed = await coordinator.ConfirmCreationAttributesAsync(fresh, allocations);
                Require(confirmed.Receipt is not null && probe.ConfirmCalls == 1,
                    "Fresh save failed after local editing.");
                var reopened = new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!;
                Require(reopened.Document.AuxiliaryState.CharacterCreationAttributesDraft!.Attributes
                    .Single(x => x.AttributeId == "BOD").Current == 4, "Cold store lost accepted attributes.");
                string retained = JsonSerializer.Serialize(draft.Allocations(state));
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => body.Value = 5));
                await ui.DrainDispatchedAsyncVoidAsync();
                Require(JsonSerializer.Serialize(draft.Allocations(state)) == retained && body.Value == 4,
                    "A slider from the old revision was reused after save.");
                IssuedPageLifecycle(page, "OnDisappearing");
            }
        });
    }

    private static async Task VerifySkillRatingCacheAsync(string contentRoot)
    {
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            foreach (bool leave in new[] { false, true })
            {
                Console.WriteLine($"SETUP cached skill sliders, leave={leave}");
                var owners = new ControlledLinkedOwner();
                SkillsRatingProbe? probe = null;
                await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners,
                    creationFinalization: true, creationAttributes: true, creationSkills: true,
                    productionCreationOverview: true, skillsDecorator: actual => probe = new(actual));
                var before = PrepareActualFinalizationReadyContext(runtime, stopBeforeQualities: true);
                Console.WriteLine("SETUP cached skill fixture persisted");
                await HydrateFinalizationOwnerAsync(runtime, owners, before);
                Console.WriteLine("SETUP cached skill fixture hydrated");
                var state = runtime.Coordinator.LoadCreationSkills().Value!;
                Console.WriteLine("SETUP cached skill state loaded");
                var group = CreationSkillsPhoneAuthority.AvailableGroups(state).First();
                string key = "group-" + new string(group.GroupId.ToLowerInvariant()
                    .Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
                var page = new CreationSkillsPage(runtime.Coordinator, state);
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing")));
                Console.WriteLine("SETUP cached skill page appeared");
                var draft = (CreationSkillsPhoneDraft)typeof(CreationSkillsPage)
                    .GetField("_draft", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
                var slider = MinimalVisible(page).OfType<Slider>().Single(x => x.AutomationId == "creation-skills-slider-" + key);
                var plus = MinimalVisible(page).OfType<Button>().Single(x => x.AutomationId == $"creation-skills-{key}-increase");
                int reads = probe!.LoadCalls + probe.PreviewCalls;
                // Local edits must not depend on a slow or failed backing-store preview.
                probe.BeforePreview = () => throw new IOException("private-preview-unavailable");
                var activeSlider = MinimalVisible(page).OfType<Slider>().First(x =>
                    x.AutomationId.StartsWith("creation-skills-slider-active-", StringComparison.Ordinal)
                    && x.Value == 0 && state.Authority.ActiveSkills.Any(skill =>
                        x.AutomationId == "creation-skills-slider-active-" + skill.SourceSkillId.ToLowerInvariant()
                        && skill.Specializations.Count > 0));
                string activeId = activeSlider.AutomationId;
                Slider ActiveSlider() => MinimalVisible(page).OfType<Slider>().Single(x => x.AutomationId == activeId);
                ((ISliderController)activeSlider).SendDragStarted();
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => activeSlider.Value = 1));
                await ui.DrainDispatchedAsyncVoidAsync();
                Require(ReferenceEquals(ActiveSlider(), activeSlider),
                    "Adding the specialization picker destroyed the slider during a drag.");
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => activeSlider.Value = 2));
                await ui.DrainDispatchedAsyncVoidAsync();
                Require(ReferenceEquals(ActiveSlider(), activeSlider) && activeSlider.Value == 2,
                    "The first-point shape change interrupted the rest of the drag.");
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((ISliderController)activeSlider).SendDragCompleted()));
                await ui.DrainDispatchedAsyncVoidAsync();
                Require(!ReferenceEquals(ActiveSlider(), activeSlider) && ActiveSlider().Value == 2,
                    "Drag completion did not retain the rating when adding the specialization picker.");
                Require(probe.LoadCalls + probe.PreviewCalls == reads && probe.ConfirmCalls == 0,
                    "A deferred slider refresh performed Core I/O or a save.");
                Console.WriteLine("PASS active-skill drag 0→1→2: one deferred specialization refresh, zero Core I/O");
                // The completed drag rebuilt the catalog; only its new controls are current.
                slider = MinimalVisible(page).OfType<Slider>().Single(x => x.AutomationId == "creation-skills-slider-" + key);
                plus = MinimalVisible(page).OfType<Button>().Single(x => x.AutomationId == $"creation-skills-{key}-increase");
                var timing = System.Diagnostics.Stopwatch.StartNew();
                Console.WriteLine("ACTION group slider to 1");
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => slider.Value = 1.2));
                await ui.DrainDispatchedAsyncVoidAsync();
                Require(slider.Value == 1 && draft.Groups.Single(x => x.GroupId == group.GroupId).Rating == 1,
                    "Group slider did not accept an integer Core projection.");
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((IButtonController)plus).SendClicked()));
                Require(slider.Value == 2 && draft.Preview!.SkillGroupPointBudget.Remaining == 0,
                    "Group + and slider lost their shared point budget.");
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => slider.Value = 3));
                await ui.DrainDispatchedAsyncVoidAsync();
                var feedback = MinimalVisible(page).OfType<Label>().Single(x => x.AutomationId == $"creation-skills-{key}-feedback");
                Require(slider.Value == 2 && feedback.IsVisible
                    && feedback.Text == CreationAllocationStrings.SkillBlocker(CharacterCreationSkillsBlockers.GroupBudgetExceeded),
                    "Over-budget slider did not reset with an adjacent explanation.");
                Require(probe.LoadCalls + probe.PreviewCalls == reads && probe.ConfirmCalls == 0,
                    "Cached group input performed Core I/O or a save.");
                Require(!runtime.Coordinator.IsCreationSkillsPreviewCurrent(draft.Preview!)
                    && !runtime.Coordinator.CanDisplayCreationSkillsPreview(draft.Preview!),
                    "A cached skills calculation acquired fresh review/confirmation authority.");
                Console.WriteLine($"PASS cached group drag/+ and budget denial: {timing.ElapsedMilliseconds}ms, zero Core I/O");
                string retained = JsonSerializer.Serialize(draft.Groups);
                if (leave) IssuedPageLifecycle(page, "OnDisappearing");
                else { var owner = owners.Current; owners.Set(ContactsOwnerB); owners.Set(owner); }
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => slider.Value = 1));
                await ui.DrainDispatchedAsyncVoidAsync();
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((IButtonController)plus).SendClicked()));
                Require(JsonSerializer.Serialize(draft.Groups) == retained && slider.Value == 2,
                    "Departure or owner A→B→A revived a cached rating action.");
                probe.BeforePreview = null;
                RequireSameRewardDocument(before, new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!);
                if (!leave) IssuedPageLifecycle(page, "OnDisappearing");
            }
        });
    }
}
