using Chummer.Android.Native;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Microsoft.Extensions.DependencyInjection;

internal static partial class AfterRunAuthorityHarness
{
    internal static async Task RunLifeModuleCompletionAsync(string contentRoot)
    {
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            foreach (string scenario in new[] { "save-career-reopen", "owner-aba-during-open", "cancel-after-commit" })
            {
                var owners = new ControlledLinkedOwner();
                LifeCompletionNativeProbe? probe = null;
                await using var runtime = new NativeRewardRuntime(contentRoot, creationBootstrap: true,
                    productionCreationOverview: true, linkedOwners: owners,
                    lifeCompletionDecorator: actual => probe = new(actual));
                await runtime.Coordinator.InitializeAsync();
                await AccountStartupTask(runtime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
                await runtime.Coordinator.CreateRunnerAsync();
                await runtime.Presenter.UpdateDialogFieldAsync("newCharacterName", "Life Modules completion", default);
                await runtime.Presenter.UpdateDialogFieldAsync("newCharacterBuildMethod", CharacterCreationBuildMethods.LifeModules, default);
                await runtime.Coordinator.ExecuteDialogActionAsync("create_character");
                Require(runtime.Coordinator.State.WorkspaceId is not null, "Native Life Modules bootstrap did not create a runner.");
                var id = runtime.Coordinator.State.WorkspaceId!.Value;
                // Seed exact typed module confirmations. These tests exercise
                // the native final allocation boundary, not the story page.
                await Task.Run(() => SeedNativeLifeSequence(runtime.Services.GetRequiredService<CharacterCreationFoundationService>(), id));
                await runtime.Presenter.LoadAsync(id, default);
                var store = new FileWorkspaceStore(runtime.StateDirectory);
                var before = store.Get(id).Value!;
                Require(runtime.Coordinator.CanOpenLifeModuleCompletion(), "Finished module sequence has no native completion entry.");
                if (scenario == "owner-aba-during-open") probe!.AfterLoad = () =>
                { owners.Set(ContactsOwnerB); owners.Set(OwnerScope.LocalSingleUser); };
                var loaded = await runtime.Coordinator.LoadLifeModuleCompletionAsync();
                if (scenario == "owner-aba-during-open")
                {
                    Require(loaded.Value is null && probe!.ConfirmCalls == 0 && store.Get(id).Value!.ContentRevision == before.ContentRevision,
                        "Owner A→B→A issued a completion editor or mutated the runner.");
                    Console.WriteLine("PASS Life Modules native completion: " + scenario);
                    continue;
                }
                Require(loaded.Value is not null, "Native completion load failed: " + string.Join(", ", loaded.Blockers));
                var state = loaded.Value!;
                var request = new CharacterCreationFoundationFinalizationPreviewRequest(state.Binding,
                    state.PendingDraft!.DraftRevision, state.PendingDraft.DraftDigest);
                var first = (await runtime.Coordinator.PreviewLifeModuleCompletionAsync(state, request)).Value!;
                Require(first.ModuleSequence is not null, "Full module sequence was not previewed.");
                request = request with
                {
                    QualityInstanceValues = first.ModuleSequence!.QualityLevels.Where(row => row.InstancePrompt is not null)
                        .ToDictionary(row => row.InstancePrompt!.PromptId, _ => "Renraku"),
                    TalentSelection = new("mundane"), AttributePurchases = []
                };
                var options = (await runtime.Coordinator.PreviewLifeModuleCompletionAsync(state, request)).Value!;
                Require(options.SkillsCatalog is not null, "Skills catalog missing after explicit talent/attribute selection.");
                var native = options.SkillsCatalog!.KnowledgeSkills.First(row => row.CanBeNativeLanguage);
                request = request with
                {
                    SkillSelection = new([new(native.SourceSkillId, native.Kind, 0, IsNativeLanguage: true)], []),
                    KarmaResourceInvestment = 0, GearSelection = [], LifestyleSelection = [], ContactSelection = [],
                    MagicSelection = new(null, null, [], [], []), StartingNuyenDiceTotal = 6
                };
                var reviewed = await runtime.Coordinator.PreviewLifeModuleCompletionAsync(state, request);
                Require(reviewed.Value is { CanApply: true, FinalizationPlan: not null },
                    "Native full allocation is not confirmable: " + string.Join(", ", reviewed.Blockers));
                var preview = reviewed.Value!;
                Require((await runtime.Coordinator.ConfirmLifeModuleCompletionAsync(preview, false)).Value is null
                    && probe!.ConfirmCalls == 0, "An unconfirmed preview dispatched a write.");
                Require((await runtime.Coordinator.ConfirmLifeModuleCompletionAsync(preview with { }, true)).Value is null,
                    "An unissued copy acquired native confirmation authority.");
                using var cancel = new CancellationTokenSource();
                if (scenario == "cancel-after-commit") probe!.AfterConfirm = cancel.Cancel;
                var applied = await runtime.Coordinator.ConfirmLifeModuleCompletionAsync(preview, true, cancel.Token);
                Require(applied.Value is { CharacterCreated: true, CharacterEffectsApplied: true },
                    "Native completion lost a committed result: " + string.Join(", ", applied.Blockers));
                var cold = new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!;
                Require(cold.ContentRevision == before.ContentRevision + 1 && cold.ContentRevision == cold.SavedRevision
                    && cold.Document.AuxiliaryState.CharacterCreationFinalizationArchive is not null,
                    "Native completion did not retain the single atomic Career transition.");
                Require((await runtime.Coordinator.ConfirmLifeModuleCompletionAsync(preview, true)).Value is null
                    && probe!.ConfirmCalls == 1, "A consumed review replayed its native command.");
                if (scenario == "save-career-reopen") Require(runtime.Coordinator.IsLifeModuleCompletionReceiptCurrent(applied.Value!),
                    "Known saved Life Modules runner did not reopen in Career.");
                else Require(applied.Blockers.Contains(CharacterCreationFinalizationBlockers.PostCommitReopenRequired),
                    "Cancellation after commit did not preserve the known result/reopen instruction.");
                ui.AssertHealthy();
                Console.WriteLine("PASS Life Modules native completion: " + scenario);
            }
        });
    }

    private static void SeedNativeLifeSequence(CharacterCreationFoundationService service, CharacterWorkspaceId id)
    {
        var start = service.Load(new(id)).Value!;
        var root = service.Preview(new(start.Binding, "Elf", new(
            "83c132b5-fcf5-4a43-b9de-6c8ab206a586", "604831d9-0fdc-4579-aa7e-bc5d99bcee5d")));
        Require(root.Value is { CanConfirm: true }, "Nationality setup blocked: " + string.Join(", ", root.Blockers));
        var selected = root.Value!;
        Require(service.Confirm(new(selected.Binding, "Elf", selected.Selection, selected.PreviewDigest, true, selected.FollowUpValues)).Value is not null,
            "Nationality setup not saved.");
        foreach (string moduleId in new[] { "924ccfd0-136c-4385-94fe-a8d7be2eb7ed", "f0393b9e-2698-4955-bd31-112b619ac7b8",
                     "5a2eee69-cedb-403e-9649-fdc9a1377374", "47bf63cf-9a2a-4008-b455-c8ab68add581" })
        {
            var journey = service.LoadJourney(new(id)).Value!;
            var option = journey.Options.Single(row => row.ModuleId == moduleId);
            var answers = option.FollowUps.ToDictionary(prompt => prompt.PromptId,
                prompt => prompt.Options.FirstOrDefault(row => row.IsEnabled)?.SourceValue ?? "Renraku");
            var module = service.PreviewModule(new(journey.Binding, journey.DraftRevision, journey.DraftDigest, new(moduleId, null), answers));
            Require(module.Value is { CanConfirm: true }, "Module setup blocked: " + string.Join(", ", module.Blockers));
            Require(service.ConfirmModule(new(module.Value!.Request, module.Value.PreviewDigest, true)).Value is not null, "Module setup not saved.");
        }
        var last = service.LoadJourney(new(id)).Value!;
        var finish = new CharacterCreationLifeModuleFinishRequest(last.Binding, last.DraftRevision, last.DraftDigest);
        var preview = service.PreviewFinishSelection(finish).Value!;
        Require(service.ConfirmFinishSelection(new(finish, preview.PreviewDigest, true)).Value is not null, "Module selection finish not saved.");
    }

    private sealed class LifeCompletionNativeProbe(IOwnerBoundCharacterCreationLifeModuleFinalizationService inner)
        : IOwnerBoundCharacterCreationLifeModuleFinalizationService
    {
        public Action? AfterLoad, AfterConfirm;
        public int ConfirmCalls;
        public CharacterCreationFoundationResult<CharacterCreationFoundationState> Load(OwnerContextStamp owner, CharacterWorkspaceId id)
        { Require(SynchronizationContext.Current is null, "Life Modules load blocks the UI context."); var r = inner.Load(owner, id); AfterLoad?.Invoke(); return r; }
        public CharacterCreationFoundationResult<CharacterCreationFoundationFinalizationPreview> Preview(OwnerContextStamp owner, CharacterCreationFoundationFinalizationPreviewRequest request)
        { Require(SynchronizationContext.Current is null, "Life Modules preview blocks the UI context."); return inner.Preview(owner, request); }
        public CharacterCreationFoundationResult<CharacterCreationFoundationFinalizationReceipt> Confirm(OwnerContextStamp owner, CharacterCreationFoundationFinalizationConfirmRequest request)
        { Require(SynchronizationContext.Current is null, "Life Modules confirmation blocks the UI context."); ConfirmCalls++; var r = inner.Confirm(owner, request); AfterConfirm?.Invoke(); return r; }
    }
}
