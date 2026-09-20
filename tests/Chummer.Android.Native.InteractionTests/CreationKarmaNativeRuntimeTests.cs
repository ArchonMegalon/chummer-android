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
        string[] scenarios = ["commit", "cancel-after-commit", "lost-return",
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
                if (scenario == "owner-aba-during-load") probe!.AfterLoad = Aba;
                var loaded = await runtime.Coordinator.LoadCreationKarmaAsync(includeSkills: true);
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
                var selection = new CreationKarmaPhoneSelection(human.OptionId, "mundane", attributes,
                    new(skillItems, []));
                if (scenario == "owner-aba-during-preview") probe!.AfterPreview = Aba;
                // Mutate the caller's lists AFTER scheduling: worker must retain
                // the original reviewed input, not the page's next edit.
                var previewTask = runtime.Coordinator.PreviewCreationKarmaAsync(state, selection);
                attributes[0] = new("LOG", 99);
                skillItems[1] = skillItems[1] with { KarmaLevels = 99 };
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
                        && draft.Skills!.Skills.Single(s => !s.IsNativeLanguage).KarmaLevels == 1,
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
        public int ConfirmCalls;
        public Action? AfterLoad, AfterPreview, AfterConfirm;
        private void AssertBackground() => Require(!ReferenceEquals(SynchronizationContext.Current, ui),
            "Synchronous Karma Core work ran on the Android UI synchronization context.");
        public CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeState> Load(
            OwnerContextStamp owner, CharacterWorkspaceId id, bool includeSkills = false)
        { AssertBackground(); var result = actual.Load(owner, id, includeSkills); AfterLoad?.Invoke(); return result; }
        public CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeQuote> Preview(
            OwnerContextStamp owner, CharacterCreationKarmaMetatypeBinding binding, string optionId,
            string? talentOptionId = null, IReadOnlyList<CharacterCreationKarmaAttributeAllocation>? attributeAllocations = null,
            CharacterCreationKarmaSkillsSelection? skillsSelection = null)
        {
            AssertBackground(); var result = actual.Preview(owner, binding, optionId, talentOptionId, attributeAllocations, skillsSelection);
            AfterPreview?.Invoke(); return result;
        }
        public CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeCommit> Confirm(
            OwnerContextStamp owner, CharacterCreationKarmaMetatypeConfirmRequest request)
        { AssertBackground(); ConfirmCalls++; var result = actual.Confirm(owner, request); AfterConfirm?.Invoke(); return result; }
    }
}
