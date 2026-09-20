using Chummer.Android.Native;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;

internal static partial class AfterRunAuthorityHarness
{
    public static async Task RunCreationKarmaAsync(string contentRoot, string? onlyScenario = null)
    {
        if (onlyScenario == "phone") { await RunKarmaPhonePagesAsync(contentRoot); return; }
        if (onlyScenario == "phone-revalidation") { await RunKarmaPhoneRevalidationAsync(contentRoot); return; }
        if (onlyScenario == "completion-admission") { await RunKarmaCompletionAdmissionAsync(contentRoot); return; }
        if (onlyScenario == "source-profile") { await RunKarmaSourceProfileAsync(contentRoot); return; }
        string[] scenarios = ["commit", "cancel-after-commit", "lost-return",
            "owner-aba-during-open", "route-left-during-open", "cancel-during-open",
            "owner-aba", "owner-aba-during-load", "owner-aba-during-preview",
            "route-left", "stale-review", "forged-review", "invalid-budget",
            "owner-aba-after-commit", "route-left-after-commit"];
        Require(onlyScenario is null || scenarios.Contains(onlyScenario), "Unknown Karma scenario.");
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            foreach (string scenario in scenarios.Where(s => onlyScenario is null || s == onlyScenario))
            {
                var owners = new ControlledLinkedOwner();
                KarmaNativeProbe? probe = null;
                await using var runtime = new NativeRewardRuntime(contentRoot, creationBootstrap: true,
                    productionCreationOverview: true, linkedOwners: owners,
                    karmaDecorator: actual => probe = new(actual, ui));
                await runtime.Coordinator.InitializeAsync();
                await AccountStartupTask(runtime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
                await runtime.Coordinator.CreateRunnerAsync();
                await runtime.Presenter.UpdateDialogFieldAsync("newCharacterName", "Karma native fixture", default);
                await runtime.Presenter.UpdateDialogFieldAsync("newCharacterBuildMethod", "Karma", default);
                await runtime.Coordinator.ExecuteDialogActionAsync("create_character");
                Require(runtime.Coordinator.State.WorkspaceId is not null
                    && runtime.Coordinator.State.Profile?.Created == false,
                    "SETUP: real native Karma bootstrap failed: " + runtime.Coordinator.State.Error);
                var id = runtime.Coordinator.State.WorkspaceId!.Value;
                var store = new FileWorkspaceStore(runtime.StateDirectory);
                var original = store.Get(id).Value!;
                bool Unchanged()
                {
                    var current = new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!;
                    return current.ContentRevision == original.ContentRevision
                        && current.SavedRevision == original.SavedRevision
                        && current.Document.Content == original.Document.Content
                        && current.Document.AuxiliaryStateDigest == original.Document.AuxiliaryStateDigest;
                }
                void Aba() { owners.Set(ContactsOwnerB); owners.Set(OwnerScope.LocalSingleUser); }
                if (scenario is "owner-aba-during-open" or "route-left-during-open" or "cancel-during-open")
                {
                    bool pageCurrent = true;
                    using var openingCancellation = new CancellationTokenSource();
                    probe!.AfterOpen = () =>
                    {
                        if (scenario == "owner-aba-during-open") Aba();
                        else if (scenario == "route-left-during-open") pageCurrent = false;
                        else openingCancellation.Cancel();
                    };
                    bool rejected = false;
                    try
                    {
                        rejected = (await runtime.Coordinator.OpenCreationKarmaAsync(true,
                            openingCancellation.Token, () => pageCurrent)).Value is null;
                    }
                    catch (OperationCanceledException) when (scenario == "cancel-during-open") { rejected = true; }
                    Require(rejected && probe.OpenCalls == 1 && probe.ConfirmCalls == 0
                        && owners.ActiveLeases == 0 && Unchanged(),
                        "A departed/canceled/owner-transitioned Open issued authority or changed the workspace.");
                    ui.AssertHealthy();
                    Console.WriteLine("PASS native Karma: " + scenario);
                    continue;
                }
                if (scenario == "owner-aba-during-load") probe!.AfterLoad = Aba;
                var loaded = await runtime.Coordinator.LoadCreationKarmaAsync(includeSkills: true, includeQualities: true);
                if (scenario == "owner-aba-during-load")
                {
                    Require(loaded.Value is null && probe!.ConfirmCalls == 0 && Unchanged(),
                        "A load completed after owner A→B→A must not issue a native editor.");
                    Console.WriteLine("PASS native Karma: " + scenario);
                    continue;
                }
                Require(loaded.Value is { SkillsCatalog: not null }
                    && runtime.Coordinator.IsCreationKarmaStateCurrent(loaded.Value),
                    "SETUP: native Karma load did not bind the real Core state: " + string.Join(",", loaded.Blockers));
                var state = loaded.Value!;
                var human = state.Options.Single(option => option.Label == "Human");
                var catalog = state.SkillsCatalog!;
                var english = catalog.KnowledgeSkills.Single(skill => skill.Name == "English");
                var pistol = catalog.ActiveSkills.Single(skill => skill.Name == "Pistols");
                var attributes = new[] { new CharacterCreationKarmaAttributeAllocation("LOG", 1) };
                var skillItems = new[] {
                    new CharacterCreationKarmaSkillAllocation(english.SourceSkillId, english.Kind, 0, IsNativeLanguage: true),
                    new CharacterCreationKarmaSkillAllocation(pistol.SourceSkillId, pistol.Kind, 1) };
                var qualityIds = new[] { state.QualitiesCatalog!.Options.Single(item => item.Name == "Unsteady Hands").OptionId };
                var selection = new CreationKarmaPhoneSelection(human.OptionId, "mundane", attributes,
                    new(skillItems, []), 10.5m, qualityIds);
                if (scenario == "owner-aba-during-preview") probe!.AfterPreview = Aba;
                // Mutate the caller's lists AFTER scheduling: worker must retain
                // the original reviewed input, not the page's next edit.
                var previewTask = runtime.Coordinator.PreviewCreationKarmaAsync(state, selection);
                attributes[0] = new("LOG", 99);
                skillItems[1] = skillItems[1] with { KarmaLevels = 99 };
                qualityIds[0] = "caller-mutated";
                var preview = await previewTask;
                if (scenario == "owner-aba-during-preview")
                {
                    Require(preview.Value is null && probe!.ConfirmCalls == 0
                        && Unchanged(), "Owner ABA during preview issued a confirmable result.");
                    Console.WriteLine("PASS native Karma: " + scenario);
                    continue;
                }
                Require(preview.Value is { CanSelect: true, Skills: not null }
                    && preview.Value.Attributes!.Allocations.Single().KarmaLevels == 1
                    && preview.Value.Skills.Selection.Skills.Single(s => !s.IsNativeLanguage).KarmaLevels == 1,
                    "Preview lost source identity, background execution or frozen input: " + string.Join(",", preview.Blockers));
                var quote = preview.Value!;
                Require(quote.Qualities is { Costs.NetKarmaSpent: -7 }
                    && quote.Qualities.Selections.Single().Name == "Unsteady Hands",
                    "Native preview lost the frozen quality source choice or its Core-owned credit.");
                var notExplicit = await runtime.Coordinator.ConfirmCreationKarmaAsync(quote, false);
                Require(notExplicit.Commit is null && probe!.ConfirmCalls == 0,
                    "A preview cannot implicitly authorize persistence.");
                using var cancel = new CancellationTokenSource();
                bool currentPage = scenario != "route-left";
                if (scenario == "owner-aba") Aba();
                if (scenario == "cancel-after-commit") probe!.AfterConfirm = cancel.Cancel;
                if (scenario == "lost-return") probe!.AfterConfirm = () => throw new IOException("Lost result after actual Core commit");
                if (scenario == "owner-aba-after-commit") probe!.AfterConfirm = Aba;
                if (scenario == "route-left-after-commit") probe!.AfterConfirm = () => currentPage = false;
                if (scenario == "stale-review")
                    Require((await runtime.Coordinator.PreviewCreationKarmaAsync(state,
                        new(human.OptionId, "mundane", [], new([skillItems[0]], [])))).Value is not null,
                        "SETUP: replacement review failed.");
                if (scenario == "forged-review") quote = quote with { };
                if (scenario == "invalid-budget")
                {
                    var blocked = await runtime.Coordinator.PreviewCreationKarmaAsync(state,
                        new(human.OptionId, "mundane", [new("LOG", 99)]));
                    Require(blocked.Value is { CanSelect: false }, "SETUP: Core must reject an over-cap allocation.");
                    quote = blocked.Value!;
                }
                var confirming = runtime.Coordinator.ConfirmCreationKarmaAsync(quote, true,
                    cancel.Token, () => currentPage);
                var doubleClick = scenario == "commit"
                    ? runtime.Coordinator.ConfirmCreationKarmaAsync(quote, true) : null;
                var confirmed = await confirming;
                if (doubleClick is not null)
                    Require((await doubleClick).Commit is null && probe!.ConfirmCalls == 1,
                        "Overlapping confirmation clicks caused two Core mutations.");
                bool committed = scenario is "commit" or "cancel-after-commit" or "lost-return"
                    or "owner-aba-after-commit" or "route-left-after-commit";
                var cold = new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!;
                Require(probe!.ConfirmCalls == (committed ? 1 : 0)
                    && cold.ContentRevision == original.ContentRevision + (committed ? 1 : 0)
                    && cold.Document.Content == original.Document.Content,
                    "The native boundary committed unexpectedly, replayed or changed character XML: " + scenario);
                if (committed)
                {
                    Require(cold.Document.AuxiliaryState.CharacterCreationKarmaMetatypeDecisions is { Count: 1 },
                        "Core did not persist exactly one pending decision.");
                    if (scenario == "lost-return")
                        Require(confirmed.Commit is null && confirmed.Blockers.Contains(RunnerSessionCoordinator.KarmaOutcomeUnknown),
                            "Unknown return must not masquerade as canceled/uncommitted or successful.");
                    else if (scenario == "owner-aba-after-commit")
                        Require(confirmed.Commit is not null && !runtime.Coordinator.CanDisplayCreationKarmaCommit(confirmed.Commit)
                            && confirmed.RefreshedState is null,
                            "A committed receipt escaped into a different owner-transition context.");
                    else
                        Require(confirmed.Commit is not null && runtime.Coordinator.CanDisplayCreationKarmaCommit(confirmed.Commit),
                            "Known commit was lost when refresh failed or cancellation arrived.");
                    if (scenario is "cancel-after-commit" or "owner-aba-after-commit" or "route-left-after-commit")
                        Require(confirmed.RefreshedState is null
                            && confirmed.Blockers.Contains(RunnerSessionCoordinator.KarmaPostCommitRefreshRequired),
                            "Canceled refresh must retain the known durable commit.");
                    var again = await runtime.Coordinator.ConfirmCreationKarmaAsync(quote, true);
                    Require(again.Commit is null && probe.ConfirmCalls == 1,
                        "A second click retried a possibly committed operation.");
                    // Reload through the real native presenter and a fresh Core
                    // store, not a fabricated pending state or copied test DTO.
                    probe.AfterConfirm = null;
                    await HydrateFinalizationOwnerAsync(runtime, owners, cold);
                    var restored = await runtime.Coordinator.LoadCreationKarmaAsync();
                    var draft = restored.Value is { } restoredState ? CreationKarmaPhoneSelection.Restore(restoredState) : null;
                    Require(draft?.TalentOptionId == "mundane"
                        && draft.Attributes!.Single().KarmaLevels == 1
                        && draft.Skills!.Skills.Single(s => !s.IsNativeLanguage).KarmaLevels == 1
                        && draft.ResourceKarmaInvestment == 10.5m
                        && draft.QualityOptionIds!.SequenceEqual(quote.Qualities!.Selections.Select(item => item.OptionId))
                        && restored.Value!.Selection!.Quote.Qualities!.Costs.NetKarmaSpent == -7
                        && restored.Value!.Selection!.Quote.Resources!.NuyenFromKarma == 21000m,
                        "Native reopen lost persisted Karma foundation or substituted Priority defaults.");
                }
                else Require(confirmed.Commit is null, "Rejected native review returned a commit.");
                Require(owners.ActiveLeases == 0, "Karma native work leaked a Core owner lease.");
                ui.AssertHealthy();
                Console.WriteLine("PASS native Karma: " + scenario);
            }
        });
    }

    private sealed class KarmaNativeProbe(IOwnerBoundCharacterCreationKarmaMetatypeService actual,
        SynchronizationContext ui) : IOwnerBoundCharacterCreationKarmaMetatypeService
    {
        public int LoadCalls, PreviewCalls, OpenCalls;
        public TimeSpan LoadTime, PreviewTime, OpenTime;
        public int ConfirmCalls, FinalConfirmCalls;
        public bool FailReads;
        public Action? AfterLoad, AfterPreview, AfterConfirm, AfterOpen, AfterFinalConfirm;
        public Func<CharacterCreationKarmaMetatypeOpen, CharacterCreationKarmaMetatypeOpen>? TransformOpen;
        private void AssertBackground() => Require(!ReferenceEquals(SynchronizationContext.Current, ui),
            "Synchronous Karma Core work ran on the Android UI synchronization context.");
        public CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeOpen> Open(
            OwnerContextStamp owner, CharacterWorkspaceId id, bool includeSkills = false, bool includeQualities = false, bool includeGear = false)
        {
            AssertBackground(); OpenCalls++;
            if (FailReads) return new(CharacterCreationFoundationOutcomes.Blocked, null,
                [CharacterCreationKarmaMetatypeBlockers.StaleBinding]);
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            var result = actual.Open(owner, id, includeSkills, includeQualities, includeGear);
            OpenTime += System.Diagnostics.Stopwatch.GetElapsedTime(started);
            if (result.Value is { } value && TransformOpen is { } transform)
                result = result with { Value = transform(value) };
            AfterOpen?.Invoke(); return result;
        }
        public CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeState> Load(
            OwnerContextStamp owner, CharacterWorkspaceId id, bool includeSkills = false, bool includeQualities = false, bool includeGear = false)
        {
            AssertBackground(); LoadCalls++;
            if (FailReads) return new(CharacterCreationFoundationOutcomes.Blocked, null,
                [CharacterCreationKarmaMetatypeBlockers.StaleBinding]);
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            var result = actual.Load(owner, id, includeSkills, includeQualities, includeGear);
            LoadTime += System.Diagnostics.Stopwatch.GetElapsedTime(started);
            AfterLoad?.Invoke(); return result;
        }
        public CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeQuote> Preview(
            OwnerContextStamp owner, CharacterCreationKarmaMetatypeBinding binding, string optionId,
            string? talentOptionId = null, IReadOnlyList<CharacterCreationKarmaAttributeAllocation>? attributeAllocations = null,
            CharacterCreationKarmaSkillsSelection? skillsSelection = null, decimal? resourceKarmaInvestment = null,
            IReadOnlyList<string>? qualityOptionIds = null,
            IReadOnlyList<CharacterCreationGearSelection>? gearSelections = null,
            IReadOnlyList<CharacterCreationKarmaContactSelection>? contactSelections = null)
        {
            AssertBackground(); PreviewCalls++;
            if (FailReads) return new(CharacterCreationFoundationOutcomes.Blocked, null,
                [CharacterCreationKarmaMetatypeBlockers.StaleBinding]);
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            var result = actual.Preview(owner, binding, optionId, talentOptionId, attributeAllocations, skillsSelection,
                resourceKarmaInvestment, qualityOptionIds, gearSelections, contactSelections);
            PreviewTime += System.Diagnostics.Stopwatch.GetElapsedTime(started);
            AfterPreview?.Invoke(); return result;
        }
        public CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeCommit> Confirm(
            OwnerContextStamp owner, CharacterCreationKarmaMetatypeConfirmRequest request)
        { AssertBackground(); ConfirmCalls++; var result = actual.Confirm(owner, request); AfterConfirm?.Invoke(); return result; }
        public CharacterCreationFoundationResult<CharacterCreationKarmaFinalizationBudgetQuote> PreviewFinalizationBudget(
            OwnerContextStamp owner, CharacterCreationKarmaMetatypeBinding binding, string quoteDigest, int diceTotal)
        { AssertBackground(); return actual.PreviewFinalizationBudget(owner, binding, quoteDigest, diceTotal); }
        public CharacterCreationFoundationResult<CharacterCreationStartingNuyenSource> LoadFinalizationStartingCash(
            OwnerContextStamp owner, CharacterCreationKarmaMetatypeBinding binding, string quoteDigest)
        { AssertBackground(); return actual.LoadFinalizationStartingCash(owner, binding, quoteDigest); }
        public CharacterCreationFoundationResult<CharacterCreationFinalizationReview> ReviewFinalization(
            OwnerContextStamp owner, CharacterCreationKarmaMetatypeBinding binding, string quoteDigest, int diceTotal,
            string? startingCashAuthorityDigest = null)
        { AssertBackground(); return actual.ReviewFinalization(owner, binding, quoteDigest, diceTotal, startingCashAuthorityDigest); }
        public CharacterCreationFoundationResult<CharacterCreationFinalizationReceipt> ConfirmFinalization(
            OwnerContextStamp owner, CharacterCreationKarmaFinalizationConfirmRequest request)
        {
            AssertBackground(); FinalConfirmCalls++;
            var result = actual.ConfirmFinalization(owner, request);
            AfterFinalConfirm?.Invoke();
            return result;
        }
    }

    private static async Task RunKarmaCompletionAdmissionAsync(string contentRoot)
    {
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            foreach (string scenario in new[] { "owner-aba", "cancel-after-commit", "route-left-after-commit", "lost-return" })
            {
                var owners = new ControlledLinkedOwner();
                KarmaNativeProbe? probe = null;
                await using var runtime = new NativeRewardRuntime(contentRoot, creationBootstrap: true,
                    productionCreationOverview: true, linkedOwners: owners,
                    karmaDecorator: actual => probe = new(actual, ui));
                await runtime.Coordinator.InitializeAsync();
                await AccountStartupTask(runtime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
                await runtime.Coordinator.CreateRunnerAsync();
                await runtime.Presenter.UpdateDialogFieldAsync("newCharacterBuildMethod", "Karma", default);
                await runtime.Coordinator.ExecuteDialogActionAsync("create_character");
                var state = (await runtime.Coordinator.LoadCreationKarmaAsync(true, includeQualities: true, includeGear: true)).Value!;
                var english = state.SkillsCatalog!.KnowledgeSkills.Single(s => s.Name == "English");
                var selection = new CreationKarmaPhoneSelection(state.Options.Single(o => o.Label == "Human").OptionId,
                    "mundane", [], new([new(english.SourceSkillId, english.Kind, 0, IsNativeLanguage: true)], []), 0, [], []);
                var preview = (await runtime.Coordinator.PreviewCreationKarmaAsync(state, selection)).Value!;
                var saved = await runtime.Coordinator.ConfirmCreationKarmaAsync(preview, true);
                Require(saved.Commit is not null, "SETUP: pending Karma save failed.");
                var current = (await runtime.Coordinator.OpenCreationKarmaAsync(true, includeQualities: true, includeGear: true)).Value!.Quote!;
                var source = (await runtime.Coordinator.LoadKarmaCompletionCashAsync(current, default, () => true)).Value!;
                var reviewed = await runtime.Coordinator.ReviewKarmaCompletionAsync(current, source, 4, default, () => true);
                Require(reviewed.Value is not null, "SETUP: Karma completion review failed: " + string.Join(",", reviewed.Blockers));
                var review = reviewed.Value!;
                var id = current.Binding.WorkspaceId;
                var before = new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!;
                bool pageCurrent = true;
                using var cancel = new CancellationTokenSource();
                if (scenario == "owner-aba") { owners.Set(ContactsOwnerB); owners.Set(OwnerScope.LocalSingleUser); }
                if (scenario == "cancel-after-commit") probe!.AfterFinalConfirm = cancel.Cancel;
                if (scenario == "route-left-after-commit") probe!.AfterFinalConfirm = () => pageCurrent = false;
                if (scenario == "lost-return") probe!.AfterFinalConfirm = () => throw new IOException("Lost completion return");
                var result = await runtime.Coordinator.ConfirmKarmaCompletionAsync(review, true, cancel.Token, () => pageCurrent);
                var after = new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!;
                if (scenario == "owner-aba")
                    Require(result.Value is null && probe!.FinalConfirmCalls == 0 && after.ContentRevision == before.ContentRevision
                        && after.Document.Content == before.Document.Content && after.Document.AuxiliaryStateDigest == before.Document.AuxiliaryStateDigest,
                        "Owner A→B→A admitted an obsolete completion intent.");
                else
                {
                    Require(probe!.FinalConfirmCalls == 1 && after.ContentRevision == before.ContentRevision + 1
                        && after.Document.AuxiliaryState.CharacterCreationFinalizationArchive?.KarmaAuthority is not null,
                        "Completion was duplicated or lost after Core committed.");
                    if (scenario == "lost-return") Require(result.Value is null && result.Blockers.Contains(RunnerSessionCoordinator.KarmaOutcomeUnknown),
                        "An unknown completion return was represented as a known save.");
                    else Require(result.Value is not null && result.Blockers.Contains(CharacterCreationFinalizationBlockers.PostCommitReopenRequired),
                        "Cancellation/navigation erased a known committed receipt.");
                    Require((await runtime.Coordinator.ConfirmKarmaCompletionAsync(review, true, default, () => true)).Value is null
                        && probe.FinalConfirmCalls == 1, "A consumed/ambiguous completion intent was replayed.");
                }
                Require(owners.ActiveLeases == 0, "Completion leaked a synchronous owner lease.");
                Console.WriteLine("PASS Karma completion admission: " + scenario);
            }
            ui.AssertHealthy();
        });
    }
}
