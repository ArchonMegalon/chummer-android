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
                int probes = 0;
                object version = new();
                var scale = new CreationIntegerSlider("scale-cache", "Rating", () => current,
                    () => (0, 6, true), _ => Task.CompletedTask, value => { probes++; return value <= 2; }, () => version);
                Require(probes == 7 && scale.RatingScale.Children.Count == 7, "Scale omitted its complete 0–6 range.");
                scale.UpdateProjection(); scale.UpdateProjection();
                Require(probes == 7, "Unchanged scale replayed speculative rule calculations.");
                version = new();
                scale.UpdateProjection();
                Require(probes == 14, "A changed draft failed to invalidate its affordances.");
                Require(scale.RatingScale.Children.OfType<Label>().Single(x => x.Text == "6").TextDecorations
                    == TextDecorations.Strikethrough, "Unavailable maximum is not visibly distinct.");
                Console.WriteLine("PASS integer slider/scale: rounding, refusal, cached affordances and invalidation");
            });
        await VerifySkillRatingCacheAsync(contentRoot);
        await VerifySavedAttributeChoiceIntentAsync(contentRoot);
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
                await Task.WhenAll(MinimalVisible(page).OfType<CreationIntegerSlider>().Select(x => x.ScalePreparation));
                Require(state.PendingDraft is null && draft.PlayerChosenAttributeIds is { Count: 0 },
                    "Fresh initialization and speculative slider scales must not mark any player attribute choice.");
                ((ISliderController)body).SendDragStarted();
                int reads = probe!.LoadCalls + probe.PreviewCalls;
                var timing = System.Diagnostics.Stopwatch.StartNew();
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => body.Value = 2.4));
                await ui.DrainDispatchedAsyncVoidAsync();
                Require(body.Value == 2 && draft.Attribute(state, "BOD")!.Current == 2
                    && draft.PlayerChosenAttributeIds is { } firstChoices
                    && firstChoices.SequenceEqual(new[] { "BOD" }, StringComparer.Ordinal),
                    "Attribute slider must snap to the accepted integer and mark only its real BOD choice.");
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => body.Value = 3));
                await ui.DrainDispatchedAsyncVoidAsync();
                Require(!MinimalVisible(page).OfType<Button>().Any(x => x.Text is "+" or "−"),
                    "Attribute allocation still exposes +/- buttons.");
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => body.Value = 4));
                await ui.DrainDispatchedAsyncVoidAsync();
                Require(body.Value == 4 && draft.NormalBudget(state).Remaining == state.NormalPointBudget.Remaining - 3,
                    "Slider and remaining normal points disagree.");
                Require(state.MaxNumberMaxAttributesCreate == 1, "SETUP: one metatype maximum expected.");
                var agility = (CreationIntegerSlider)SliderFor("AGI");
                Label AgilityMax() => agility.RatingScale.Children.OfType<Label>().Single(x =>
                    x.Text == agility.Maximum.ToString(System.Globalization.CultureInfo.CurrentCulture));
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => body.Value = body.Maximum));
                await ui.DrainDispatchedAsyncVoidAsync();
                await agility.ScalePreparation;
                Require(AgilityMax().TextDecorations == TextDecorations.Strikethrough,
                    "Spending the one metatype maximum did not grey the other attribute's maximum.");
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => agility.Value = agility.Maximum));
                await ui.DrainDispatchedAsyncVoidAsync();
                var attributeFeedback = AllocationFooter(page, "creation-attributes-save-status");
                Require(agility.Value < agility.Maximum && attributeFeedback.TextColor == NativeTheme.Danger
                    && !string.IsNullOrWhiteSpace(attributeFeedback.Text),
                    "Rejected attribute allocation lacks a fixed, visible explanation.");
                Require(draft.PlayerChosenAttributeIds is { } refusedChoices
                    && refusedChoices.SequenceEqual(new[] { "BOD" }, StringComparer.Ordinal),
                    "A rejected AGI maximum or speculative scale probe marked an unaccepted attribute choice.");
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => body.Value = body.Minimum));
                await ui.DrainDispatchedAsyncVoidAsync();
                Require(body.Value == body.Minimum
                    && draft.Allocations(state).Single(row => row.AttributeId == "BOD").PriorityPoints == 0
                    && draft.PlayerChosenAttributeIds is { } returnedChoices
                    && returnedChoices.SequenceEqual(new[] { "BOD" }, StringComparer.Ordinal),
                    "Returning BOD to its minimum erased the player's explicit attribute choice.");
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => body.Value = 4));
                await ui.DrainDispatchedAsyncVoidAsync();
                Require(attributeFeedback.TextColor != NativeTheme.Danger,
                    "An accepted attribute retained the old refusal.");
                await agility.ScalePreparation;
                Require(AgilityMax().TextDecorations == TextDecorations.None,
                    "Returning the metatype maximum did not re-enable the other attribute's maximum.");
                int startingMagic = draft.Attribute(state, "MAG")!.Current;
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => magic.Value = startingMagic + 1));
                await ui.DrainDispatchedAsyncVoidAsync();
                Require(magic.Value == startingMagic + 1
                    && draft.SpecialBudget(state).Remaining == state.SpecialPointBudget.Remaining - 1
                    && draft.PlayerChosenAttributeIds is { } acceptedChoices
                    && acceptedChoices.SequenceEqual(new[] { "BOD", "MAG" }, StringComparer.Ordinal),
                    "Magic slider must consume its special pool and retain exactly the accepted BOD/MAG choices.");
                Require(probe.LoadCalls + probe.PreviewCalls == reads && probe.ConfirmCalls == 0,
                    "Attribute editing performed store/source I/O or an implicit save.");
                RequireSameRewardDocument(before, new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!);
                Console.WriteLine($"PASS {method} normal/special integer sliders: {timing.ElapsedMilliseconds}ms, zero Core I/O");

                var allocations = draft.Allocations(state);
                var playerChoices = draft.PlayerChosenAttributeIds;
                var local = draft.PreviewPrepared(state, allocations).Value!;
                Require(local.PlayerChosenAttributeIds is { } localChoices
                    && localChoices.SequenceEqual(playerChoices!, StringComparer.Ordinal)
                    && !coordinator.CanOfferCreationAttributesConfirmation(local, allocations),
                    "A local cached projection lost player intent or acquired confirmation authority.");
                var fresh = coordinator.PreviewCreationAttributes(state.Binding, allocations, playerChoices).Value!;
                Require(probe.PreviewCalls > 0 && coordinator.CanOfferCreationAttributesConfirmation(fresh, allocations)
                    && fresh.PlayerChosenAttributeIds is { } reviewedChoices
                    && reviewedChoices.SequenceEqual(playerChoices!, StringComparer.Ordinal),
                    "Review did not retain a fresh Core preview of the exact player-choice snapshot.");
                var forgedIntent = fresh with { PlayerChosenAttributeIds = new[] { "BOD" } };
                Require(!CreationAttributesPhoneAuthority.CanonicallyEquals(fresh, forgedIntent)
                    && !CreationAttributesPhoneAuthority.CanonicallyEquals(fresh,
                        fresh with { PlayerChosenAttributeIds = null }),
                    "Equal preview digests must not hide changed or erased player-choice metadata.");
                var confirmed = await coordinator.ConfirmCreationAttributesAsync(fresh, allocations);
                Require(confirmed.Receipt is not null && probe.ConfirmCalls == 1,
                    "Fresh save failed after local editing.");
                var reopened = new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!;
                var savedDraft = reopened.Document.AuxiliaryState.CharacterCreationAttributesDraft!;
                Require(savedDraft.Attributes.Single(x => x.AttributeId == "BOD").Current == 4
                    && savedDraft.PlayerChosenAttributeIds is { } savedChoices
                    && savedChoices.SequenceEqual(playerChoices!, StringComparer.Ordinal),
                    "Cold store lost accepted attributes or their exact player-choice metadata.");
                var reloaded = coordinator.LoadCreationAttributes().Value!;
                var coldDraft = new CreationAttributesPhoneDraft();
                coldDraft.Bind(reloaded, coordinator.State);
                Require(coldDraft.PlayerChosenAttributeIds is { } coldChoices
                    && coldChoices.SequenceEqual(playerChoices!, StringComparer.Ordinal)
                    && CreationAttributesPhoneAuthority.ReceiptMatches(
                        confirmed.Receipt!, fresh, allocations, reloaded, coordinator.State)
                    && !CreationAttributesPhoneAuthority.ReceiptMatches(
                        confirmed.Receipt!, forgedIntent, allocations, reloaded, coordinator.State),
                    "Cold binding or receipt admission ignored the persisted player-choice identity.");
                string retained = JsonSerializer.Serialize(draft.Allocations(state));
                string retainedChoices = JsonSerializer.Serialize(draft.PlayerChosenAttributeIds);
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => body.Value = 5));
                await ui.DrainDispatchedAsyncVoidAsync();
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => agility.Value = agility.Minimum + 1));
                await ui.DrainDispatchedAsyncVoidAsync();
                Require(JsonSerializer.Serialize(draft.Allocations(state)) == retained && body.Value == 4
                    && JsonSerializer.Serialize(draft.PlayerChosenAttributeIds) == retainedChoices,
                    "A slider from the old revision changed allocations or newly marked AGI after save.");
                RequireSameRewardDocument(reopened, new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!);
                IssuedPageLifecycle(page, "OnDisappearing");
            }
        });
    }

    private static async Task VerifySavedAttributeChoiceIntentAsync(string contentRoot)
    {
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            foreach (bool legacy in new[] { true, false })
            {
                Console.WriteLine($"SETUP saved attribute choice intent, legacy={legacy}");
                var owners = new ControlledLinkedOwner();
                AttributesCommitProbe? probe = null;
                await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners,
                    creationFinalization: true, creationAttributes: true, productionCreationOverview: true,
                    attributesDecorator: actual => probe = new(actual));
                var before = PrepareActualFinalizationReadyContext(runtime,
                    stopBeforeAttributes: !legacy, stopBeforeSkills: legacy);
                await HydrateFinalizationOwnerAsync(runtime, owners, before);
                var coordinator = runtime.Coordinator;
                var state = coordinator.LoadCreationAttributes().Value!;
                if (!legacy)
                {
                    // Establish a real saved, known-empty baseline through Core,
                    // not by rewriting historical auxiliary state.
                    var seed = new CreationAttributesPhoneDraft();
                    seed.Bind(state, coordinator.State);
                    var allocations = seed.Allocations(state);
                    var preview = coordinator.PreviewCreationAttributes(state.Binding, allocations,
                        Array.Empty<string>()).Value!;
                    Require(preview.PlayerChosenAttributeIds is { Count: 0 }
                        && coordinator.CanOfferCreationAttributesConfirmation(preview, allocations),
                        "SETUP: actual Core did not admit the known-empty attribute baseline.");
                    var confirmed = await coordinator.ConfirmCreationAttributesAsync(preview, allocations);
                    Require(confirmed.Receipt is not null, "SETUP: known-empty attribute baseline was not saved.");
                    before = new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!;
                    state = coordinator.LoadCreationAttributes().Value!;
                }
                var originalDraft = before.Document.AuxiliaryState.CharacterCreationAttributesDraft!;
                Require(state.PendingDraft is not null && (legacy
                        ? originalDraft.PlayerChosenAttributeIds is null
                        : originalDraft.PlayerChosenAttributeIds is { Count: 0 }),
                    "SETUP: saved legacy/known-empty attribute intent is not distinct.");
                var page = new CreationAttributesPage(coordinator, state);
                var window = new Window(new NavigationPage(page));
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing")));
                var draft = (CreationAttributesPhoneDraft)typeof(CreationAttributesPage)
                    .GetField("_draft", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
                Slider BodySlider() => MinimalVisible(page).OfType<Slider>().Single(x =>
                    x.AutomationId == "creation-attributes-slider-bod");
                await Task.WhenAll(MinimalVisible(page).OfType<CreationIntegerSlider>().Select(x => x.ScalePreparation));
                Require(legacy ? draft.PlayerChosenAttributeIds is null
                        : draft.PlayerChosenAttributeIds is { Count: 0 },
                    "Binding or preparing saved sliders invented player-choice intent.");
                var body = BodySlider();
                Require(body.Value == body.Minimum, "SETUP: saved BOD must start at its minimum.");
                string originalAllocations = JsonSerializer.Serialize(draft.Allocations(state));
                int reads = probe!.LoadCalls + probe.PreviewCalls;
                int confirms = probe.ConfirmCalls;
                ((ISliderController)body).SendDragStarted();
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => body.Value = body.Minimum + 1));
                await ui.DrainDispatchedAsyncVoidAsync();
                Require(body.Value == body.Minimum + 1 && (legacy
                        ? draft.PlayerChosenAttributeIds is null
                        : draft.PlayerChosenAttributeIds is { } accepted
                            && accepted.SequenceEqual(new[] { "BOD" }, StringComparer.Ordinal)),
                    "Accepted BOD input promoted legacy intent or failed to mark known intent.");
                if (!legacy)
                {
                    await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => body.Value = body.Minimum));
                    await ui.DrainDispatchedAsyncVoidAsync();
                    Require(JsonSerializer.Serialize(draft.Allocations(state)) == originalAllocations
                        && draft.PlayerChosenAttributeIds is { } returned
                        && returned.SequenceEqual(new[] { "BOD" }, StringComparer.Ordinal),
                        "Returning saved BOD to its minimum erased the metadata-only choice.");
                }
                Require(probe.LoadCalls + probe.PreviewCalls == reads && probe.ConfirmCalls == confirms,
                    "Saved attribute gestures performed Core I/O or wrote before autosave.");
                RequireSameRewardDocument(before, new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!);
                Require(await page.FlushAutosaveAsync() && probe.ConfirmCalls == confirms + 1,
                    "Actual page autosave did not confirm exactly one numeric or intent-only change.");
                var cold = new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!;
                var saved = cold.Document.AuxiliaryState.CharacterCreationAttributesDraft!;
                Require(cold.ContentRevision == before.ContentRevision + 1 && (legacy
                        ? saved.PlayerChosenAttributeIds is null
                            && saved.Attributes.Single(x => x.AttributeId == "BOD").Current
                                == originalDraft.Attributes.Single(x => x.AttributeId == "BOD").Current + 1
                        : saved.PlayerChosenAttributeIds is { } savedChoices
                            && savedChoices.SequenceEqual(new[] { "BOD" }, StringComparer.Ordinal)
                            && JsonSerializer.Serialize(saved.Allocations)
                                == JsonSerializer.Serialize(originalDraft.Allocations)),
                    "Cold read lost legacy null intent or the known-empty metadata-only revision.");
                Require(legacy ? draft.PlayerChosenAttributeIds is null
                        : draft.PlayerChosenAttributeIds is { } rebound
                            && rebound.SequenceEqual(new[] { "BOD" }, StringComparer.Ordinal),
                    "Autosave refresh changed the admitted player-choice metadata.");
                if (!legacy)
                {
                    // A second real gesture reaches the same numeric and intent
                    // state: exercise duplicate review, not only a clean flush.
                    body = BodySlider();
                    ((ISliderController)body).SendDragStarted();
                    await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => body.Value = body.Minimum + 1));
                    await ui.DrainDispatchedAsyncVoidAsync();
                    Require(body.Value == body.Minimum + 1, "Duplicate-intent gesture was not accepted.");
                    await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => body.Value = body.Minimum));
                    await ui.DrainDispatchedAsyncVoidAsync();
                    int previews = probe.PreviewCalls;
                    Require(await page.FlushAutosaveAsync() && probe.PreviewCalls > previews
                        && probe.ConfirmCalls == confirms + 1,
                        "Same allocation and intent failed duplicate review or wrote another revision.");
                    RequireSameRewardDocument(cold, new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!);
                }
                IssuedPageLifecycle(page, "OnDisappearing");
                await page.FlushAutosaveAsync();
                await Task.WhenAll(MinimalVisible(page).OfType<CreationIntegerSlider>().Select(x => x.ScalePreparation));
                await ui.DrainDispatchedAsyncVoidAsync();
                Console.WriteLine($"PASS saved attribute choice intent: legacy={legacy}, actual autosave and cold persistence");
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
                Require(!MinimalVisible(page).OfType<Button>().Any(x => x.Text is "+" or "−"),
                    "Skill allocation still exposes +/- buttons.");
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
                ((ISliderController)slider).SendDragStarted();
                await Task.WhenAll(MinimalVisible(page).OfType<CreationIntegerSlider>().Select(x => x.ScalePreparation));

                var ratingSlider = (CreationIntegerSlider)slider;
                Label Tick(int value) => ratingSlider.RatingScale.Children.OfType<Label>().Single(x => x.Text == value.ToString());
                var otherGroup = MinimalVisible(page).OfType<CreationIntegerSlider>().First(x =>
                    x.AutomationId.StartsWith("creation-skills-slider-group-", StringComparison.Ordinal)
                    && !ReferenceEquals(x, slider) && x.Value == 0
                    && x.RatingScale.Children.OfType<Label>().Any(t => t.Text == "1" && t.TextDecorations == TextDecorations.None));
                Label OtherOne() => otherGroup.RatingScale.Children.OfType<Label>().Single(x => x.Text == "1");
                Require(Tick(2).TextDecorations == TextDecorations.None
                    && Tick(3).TextDecorations == TextDecorations.Strikethrough,
                    "Group scale does not distinguish affordable 2 from unavailable 3.");
                var timing = System.Diagnostics.Stopwatch.StartNew();
                Console.WriteLine("ACTION group slider to 1");
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => slider.Value = 1.2));
                await ui.DrainDispatchedAsyncVoidAsync();
                Require(slider.Value == 1 && draft.Groups.Single(x => x.GroupId == group.GroupId).Rating == 1,
                    "Group slider did not accept an integer Core projection.");
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => slider.Value = 2));
                await ui.DrainDispatchedAsyncVoidAsync();
                Require(slider.Value == 2 && draft.Preview!.SkillGroupPointBudget.Remaining == 0,
                    "Group slider lost its point budget.");
                await Task.WhenAll(ratingSlider.ScalePreparation, otherGroup.ScalePreparation);
                Require(OtherOne().TextDecorations == TextDecorations.Strikethrough,
                    "Spending the shared group pool did not grey another group's first point.");
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => slider.Value = 3));
                await ui.DrainDispatchedAsyncVoidAsync();
                var feedback = AllocationFooter(page, "creation-skills-save-status");
                Require(slider.Value == 2 && feedback.IsVisible
                    && feedback.Text == CreationAllocationStrings.SkillBlocker(CharacterCreationSkillsBlockers.GroupBudgetExceeded),
                    "Over-budget slider did not reset with a fixed, visible explanation.");
                Require(Tick(2).TextDecorations == TextDecorations.None
                    && Tick(3).TextDecorations == TextDecorations.Strikethrough,
                    "Budget refusal changed the accepted scale.");
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => slider.Value = 1));
                await ui.DrainDispatchedAsyncVoidAsync();
                Require(feedback.Text == PhoneStrings.Get("CreationAutosavePending", "Not saved yet…")
                    && feedback.TextColor != NativeTheme.Danger,
                    "An accepted allocation retained the old budget refusal.");
                await Task.WhenAll(ratingSlider.ScalePreparation, otherGroup.ScalePreparation);
                Require(Tick(2).TextDecorations == TextDecorations.None,
                    "Returning a group point did not re-enable its affordable value.");
                Require(OtherOne().TextDecorations == TextDecorations.None,
                    "Returning a shared group point did not re-enable the other group's first point.");
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => slider.Value = 2));
                await ui.DrainDispatchedAsyncVoidAsync();
                Require(probe.LoadCalls + probe.PreviewCalls == reads && probe.ConfirmCalls == 0,
                    "Cached group input performed Core I/O or a save.");
                Require(!runtime.Coordinator.IsCreationSkillsPreviewCurrent(draft.Preview!)
                    && !runtime.Coordinator.CanDisplayCreationSkillsPreview(draft.Preview!),
                    "A cached skills calculation acquired fresh review/confirmation authority.");
                Console.WriteLine($"PASS cached group drag and budget denial: {timing.ElapsedMilliseconds}ms, zero Core I/O");
                string retained = JsonSerializer.Serialize(draft.Groups);
                if (leave) IssuedPageLifecycle(page, "OnDisappearing");
                else { var owner = owners.Current; owners.Set(ContactsOwnerB); owners.Set(owner); }
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => slider.Value = 1));
                await ui.DrainDispatchedAsyncVoidAsync();
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => slider.Value = 0));
                await ui.DrainDispatchedAsyncVoidAsync();
                // Departure now starts autosave and disables the retired editor.
                // A programmatic Value write on a disabled control is not a user
                // input; it must still leave the admitted draft unchanged.
                Require(JsonSerializer.Serialize(draft.Groups) == retained && (slider.Value == 2 || !slider.IsEnabled),
                    "Departure or owner A→B→A revived a cached rating action.");
                if (!leave) IssuedPageLifecycle(page, "OnDisappearing");
                await page.FlushAutosaveAsync();
                await Task.WhenAll(MinimalVisible(page).OfType<CreationIntegerSlider>().Select(x => x.ScalePreparation)
                    .Append(((CreationIntegerSlider)activeSlider).ScalePreparation)
                    .Append(ratingSlider.ScalePreparation).Append(otherGroup.ScalePreparation));
                await ui.DrainDispatchedAsyncVoidAsync();
                RequireSameRewardDocument(before, new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!);
                probe.BeforePreview = null;
            }
        });
    }

    private static Label AllocationFooter(ContentPage page, string id)
    {
        var feedback = MinimalVisible(page).OfType<Label>().Single(x => x.AutomationId == id);
        for (Element? parent = feedback.Parent; parent is not null; parent = parent.Parent)
            Require(parent is not ScrollView, "Allocation feedback can scroll behind the fixed footer.");
        return feedback;
    }
}
