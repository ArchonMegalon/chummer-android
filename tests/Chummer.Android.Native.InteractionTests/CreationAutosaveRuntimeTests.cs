using System.Reflection;
using System.Text.Json;
using Chummer.Android.Native;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Workspaces;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    internal static async Task RunCreationAutosaveAsync(string contentRoot)
    {
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            int scaleUiThread = Environment.CurrentManagedThreadId;
            object version = new();
            bool allowed = false;
            int checks = 0;
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var release = new ManualResetEventSlim();
            var scale = new CreationIntegerSlider("autosave-background-scale", "Rating", () => 1,
                () => (1, 6, true), _ => Task.CompletedTask, selectionVersion: () => version,
                prepareSelection: rating =>
                {
                    Require(Environment.CurrentManagedThreadId == scaleUiThread, "Draft capture left the UI thread.");
                    bool captured = allowed;
                    return () =>
                    {
                        Require(Environment.CurrentManagedThreadId != scaleUiThread, "Scale rules blocked the UI thread.");
                        Interlocked.Increment(ref checks);
                        if (!captured)
                        {
                            entered.TrySetResult();
                            Require(release.Wait(TimeSpan.FromSeconds(10)), "Scale worker was not released.");
                        }
                        return captured;
                    };
                });
            Task oldScale = scale.ScalePreparation;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Require(!oldScale.IsCompleted, "Scale work was not blocked independently of the UI.");
                allowed = true;
                version = new();
                scale.UpdateProjection();
                release.Set();
                await oldScale.WaitAsync(TimeSpan.FromSeconds(5));
                await scale.ScalePreparation.WaitAsync(TimeSpan.FromSeconds(5));
                Require(checks == 7, "Retired scale continued evaluating its remaining ratings.");
                Require(scale.RatingScale.Children.OfType<Label>().All(x => x.TextDecorations == TextDecorations.None),
                    "A retired scale result overwrote current affordances.");
                int completedChecks = checks;
                scale.UpdateProjection();
                Require(checks == completedChecks, "An unchanged allocation repeated scale evaluation.");
            }
            finally { release.Set(); }
            Console.WriteLine("PASS background scale: responsive UI, immutable capture, stale rejection, cached result");
            foreach (string scenario in new[] { "saved", "departure", "unknown", "owner-aba", "dependent-skills" })
            {
                var owners = new ControlledLinkedOwner();
                AttributesCommitProbe? probe = null;
                await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners,
                    creationFinalization: true, creationAttributes: true, creationSkills: true,
                    productionCreationOverview: true, attributesDecorator: actual => probe = new(actual));
                var before = PrepareActualFinalizationReadyContext(runtime,
                    stopBeforeAttributes: scenario != "dependent-skills", stopBeforeQualities: scenario == "dependent-skills");
                await HydrateFinalizationOwnerAsync(runtime, owners, before);
                var state = runtime.Coordinator.LoadCreationAttributes().Value!;
                var page = new CreationAttributesPage(runtime.Coordinator, state);
                var navigation = new NavigationPage(page);
                var window = new Window(navigation);
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing")));
                int uiThread = Environment.CurrentManagedThreadId;
                probe!.BeforeRead = () => Require(Environment.CurrentManagedThreadId != uiThread,
                    "Autosave performed a Core source/store read on the UI thread.");
                Require(!MinimalVisible(page).OfType<Button>().Any(button =>
                    button.AutomationId == "creation-attributes-prepare-preview"), "Attributes still requires review.");
                var slider = MinimalVisible(page).OfType<Slider>().Single(x => x.AutomationId == "creation-attributes-slider-bod");
                ((ISliderController)slider).SendDragStarted();
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => slider.Value = 2));
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => slider.Value = 3));
                await ui.DrainDispatchedAsyncVoidAsync();
                Require(probe!.ConfirmCalls == 0, "A live drag wrote intermediate values.");
                ((ISliderController)slider).SendDragCompleted();
                if (scenario == "unknown") probe.AfterConfirm = () => throw new IOException("synthetic-lost-result");
                if (scenario == "owner-aba") { var owner = owners.Current; owners.Set(ContactsOwnerB); owners.Set(owner); }
                if (scenario == "departure") IssuedPageLifecycle(page, "OnDisappearing");
                if (scenario == "saved")
                {
                    // No explicit flush: prove the actual idle timer initiates persistence.
                    var status = MinimalVisible(page).OfType<Label>().Single(x => x.AutomationId == "creation-attributes-save-status");
                    for (int wait = 0; wait < 400 && status.Text != PhoneStrings.Get("CreationAutosaveSaved", "Saved"); wait++)
                        await Task.Delay(50);
                    Require(status.Text == PhoneStrings.Get("CreationAutosaveSaved", "Saved") && probe.ConfirmCalls == 1,
                        "The automatic idle save never completed.");
                }
                bool saved = await page.FlushAutosaveAsync();
                Require(saved == (scenario is "saved" or "departure" or "dependent-skills"), "Autosave misreported " + scenario);
                int confirms = probe.ConfirmCalls;
                Require(confirms == (scenario == "owner-aba" ? 0 : 1), "Autosave commit count is wrong.");
                Require(await page.FlushAutosaveAsync() == saved && probe.ConfirmCalls == confirms,
                    "Autosave replayed a completed or uncertain write.");
                var cold = new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!;
                if (scenario == "owner-aba") RequireSameRewardDocument(before, cold);
                else Require(cold.Document.AuxiliaryState.CharacterCreationAttributesDraft!.Attributes
                    .Single(x => x.AttributeId == "BOD").Current == 3, "Autosave lost the final attribute value on cold read.");
                if (scenario is "saved" or "dependent-skills")
                {
                    var next = MinimalVisible(page).OfType<Button>().Single(x => x.AutomationId == "creation-attributes-continue");
                    await JoinIssuedPageAsync(ui.BeginAsyncVoid(() =>
                    {
                        ((IButtonController)next).SendClicked();
                        ((IButtonController)next).SendClicked();
                    }));
                    bool expectedPage = scenario == "dependent-skills"
                        ? navigation.Navigation.NavigationStack.Last() is CreationSkillsReReviewPage
                        : navigation.Navigation.NavigationStack.Last() is CreationSkillsPage;
                    Require(expectedPage && navigation.Navigation.NavigationStack.Count == 2 && probe.ConfirmCalls == 1,
                        "Continue did not advance directly, or saved twice.");
                    if (scenario == "dependent-skills")
                        Require(JsonSerializer.Serialize(before.Document.AuxiliaryState.CharacterCreationSkillsDraft)
                            == JsonSerializer.Serialize(cold.Document.AuxiliaryState.CharacterCreationSkillsDraft)
                            && new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!.ContentRevision
                                == cold.ContentRevision,
                            "Opening the Skills repair discarded or silently rewrote historical choices.");
                }
                IssuedPageLifecycle(page, "OnDisappearing");
                Console.WriteLine("PASS attribute autosave: " + scenario);
            }

            var skillOwners = new ControlledLinkedOwner();
            SkillsRatingProbe? skillsProbe = null;
            await using var skillsRuntime = new NativeRewardRuntime(contentRoot, linkedOwners: skillOwners,
                creationFinalization: true, creationAttributes: true, creationSkills: true,
                productionCreationOverview: true, skillsDecorator: actual => skillsProbe = new(actual));
            var skillsBefore = PrepareActualFinalizationReadyContext(skillsRuntime, stopBeforeQualities: true);
            await HydrateFinalizationOwnerAsync(skillsRuntime, skillOwners, skillsBefore);
            var authority = skillsRuntime.Coordinator.LoadCreationSkills().Value!;
            var skillsPage = new CreationSkillsPage(skillsRuntime.Coordinator, authority);
            var skillsWindow = new Window(new NavigationPage(skillsPage));
            await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(skillsPage, "OnAppearing")));
            int skillsUiThread = Environment.CurrentManagedThreadId;
            skillsProbe!.BeforeLoad = skillsProbe.BeforePreview = () => Require(
                Environment.CurrentManagedThreadId != skillsUiThread,
                "Skill autosave performed a Core source/store read on the UI thread.");
            Require(!MinimalVisible(skillsPage).OfType<Button>().Any(x => x.AutomationId is
                "creation-skills-review" or "creation-skills-review-top"), "Skills still requires review.");
            var source = CreationSkillsPhoneAuthority.AvailableActiveSkills(authority).Skip(20)
                .First(x => x.Specializations.Count > 6);
            string token = new(source.SourceSkillId.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
            var search = MinimalVisible(skillsPage).OfType<SearchBar>().Single();
            var initialSlider = MinimalVisible(skillsPage).OfType<Slider>().First();
            // Real keyboard input delivers successive prefixes, not one full
            // query assignment. None may synchronously rebuild native controls.
            for (int length = 1; length <= source.Name.Length; length++)
            {
                search.Text = source.Name[..length];
                Require(ReferenceEquals(initialSlider, MinimalVisible(skillsPage).OfType<Slider>().First()),
                    "Typing a search prefix synchronously rebuilt the native catalog.");
            }
            await skillsPage.SearchRefresh.WaitAsync(TimeSpan.FromSeconds(5));
            Require(ReferenceEquals(search, MinimalVisible(skillsPage).OfType<SearchBar>().Single()),
                "Filtering replaced the focused search control.");
            Slider SkillSlider() => MinimalVisible(skillsPage).OfType<Slider>().Single(x =>
                x.AutomationId == "creation-skills-slider-active-" + token);
            var skillSlider = SkillSlider();
            ((ISliderController)skillSlider).SendDragStarted();
            await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => skillSlider.Value = 2));
            await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((ISliderController)skillSlider).SendDragCompleted()));
            await ui.DrainDispatchedAsyncVoidAsync();
            var picker = MinimalVisible(skillsPage).OfType<Picker>().Single(x =>
                x.AutomationId == "creation-skill-specialization-" + token);
            Require(picker.ItemsSource.Count == source.Specializations.Count + 1,
                "The specialization picker truncated admitted options.");
            await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => picker.SelectedIndex = source.Specializations.Count));
            Require(!MinimalVisible(skillsPage).OfType<Button>().Any(x =>
                x.AutomationId == "creation-skill-specialization-preview-" + token), "A dead Preview action remains.");
            Require(await skillsPage.FlushAutosaveAsync(), "Skill/specialization autosave failed.");
            Require(skillsProbe!.ConfirmCalls == 1, "Skill gesture and specialization were not coalesced.");
            await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => picker.SelectedIndex = 1));
            Require(await skillsPage.FlushAutosaveAsync() && skillsProbe.ConfirmCalls == 1,
                "A retired specialization picker dispatched another save.");
            var releaseSearchGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task gateOwner = (Task)typeof(NativePageBase).GetMethod("RunWithConditionalRefreshAsync",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(skillsPage,
                    [new Func<Task<bool>>(() => releaseSearchGate.Task)])!;
            try
            {
                search.Text = "no-such-skill-12345";
                await skillsPage.SearchRefresh.WaitAsync(TimeSpan.FromSeconds(5));
                Require(SkillSlider().Value == 2, "Search rebuilt the catalog while another action owned the gate.");
            }
            finally { releaseSearchGate.TrySetResult(false); }
            await gateOwner.WaitAsync(TimeSpan.FromSeconds(5));
            await ui.DrainDispatchedAsyncVoidAsync();
            Require(!MinimalVisible(skillsPage).OfType<Slider>().Any(), "Search does not filter the whole catalog.");
            search.Text = source.Name;
            await skillsPage.SearchRefresh.WaitAsync(TimeSpan.FromSeconds(5));
            Require(SkillSlider().Value == 2, "Search erased a saved rating.");
            var coldSkills = new FileWorkspaceStore(skillsRuntime.StateDirectory).Get(skillsRuntime.Id).Value!
                .Document.AuxiliaryState.CharacterCreationSkillsDraft!;
            Require(coldSkills.Skills.Single(x => x.SourceSkillId == source.SourceSkillId).SpecializationOptionId
                == source.Specializations.Last().OptionId, "Cold reopen lost the full-catalog specialization selection.");
            var retainedSlider = SkillSlider();
            search.Text = "departed-search-must-not-render";
            IssuedPageLifecycle(skillsPage, "OnDisappearing");
            await skillsPage.SearchRefresh.WaitAsync(TimeSpan.FromSeconds(5));
            Require(ReferenceEquals(retainedSlider, SkillSlider()), "A departed search rebuilt the old page.");
            Console.WriteLine("PASS coalesced searchable skill catalog, departure cancellation, immediate specialization, autosave and cold read");
        });
    }
}
