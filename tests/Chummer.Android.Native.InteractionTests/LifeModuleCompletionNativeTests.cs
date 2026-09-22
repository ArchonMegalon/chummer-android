using Chummer.Android.Native;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using System.Text.Json;

internal static partial class AfterRunAuthorityHarness
{
    internal static async Task RunLifeModuleCompletionPagesAsync(string contentRoot)
    {
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            var owners = new ControlledLinkedOwner();
            LifeCompletionNativeProbe? probe = null;
            await using var runtime = new NativeRewardRuntime(contentRoot, creationBootstrap: true,
                productionCreationOverview: true, linkedOwners: owners, lifeCompletionDecorator: actual => probe = new(actual));
            await runtime.Coordinator.InitializeAsync();
            await AccountStartupTask(runtime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
            await runtime.Coordinator.CreateRunnerAsync();
            await runtime.Presenter.UpdateDialogFieldAsync("newCharacterName", "Life Modules phone choices", default);
            await runtime.Presenter.UpdateDialogFieldAsync("newCharacterBuildMethod", CharacterCreationBuildMethods.LifeModules, default);
            await runtime.Coordinator.ExecuteDialogActionAsync("create_character");
            var id = runtime.Coordinator.State.WorkspaceId!.Value;
            await Task.Run(() => SeedNativeLifeSequence(runtime.Services.GetRequiredService<CharacterCreationFoundationService>(), id));
            await runtime.Presenter.LoadAsync(id, default);
            var before = new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!;
            var drafts = new LifeModuleCompletionDraftStore(runtime.StateDirectory);
            var root = new LifeModuleCompletionPage(runtime.Coordinator, store: drafts);
            var navigation = new NavigationPage(new ContentPage());
            await navigation.PushAsync(root, false);
            var window = new Window(navigation);
            using var alerts = new IssuedPageAlerts(root, window);
            await alerts.PreflightAsync();
            await Appear();
            Require(Session().Input is { TalentSelection: null, AttributePurchases: null, SkillSelection: null,
                GearSelection: null, LifestyleSelection: null, ContactSelection: null, MagicSelection: null },
                "Opening the completion wizard silently chose a talent or empty purchases.");
            await Click("life-open-qualities");
            foreach (var entry in IssuedElements(Current()).OfType<Entry>().Where(e => e.AutomationId?.StartsWith("life-quality-", StringComparison.Ordinal) == true)) entry.Text = "Renraku";
            await Click("life-review-qualities");
            await Back();
            await Click("life-open-talent");
            await Click("life-talent-mundane");
            await Back();
            await Click("life-open-attributes");
            await Click("life-begin-attributes");
            var oldAgility = Element<Stepper>("life-attribute-AGI");
            oldAgility.Value = 1;
            await Click("life-review-attributes");
            await Back();
            await Click("life-open-attributes");
            oldAgility.Value = 2;
            Require(Element<Stepper>("life-attribute-AGI").Value == 1, "Departed attribute control changed the current draft.");
            await Back();
            await Click("life-open-skills");
            await Click("life-begin-skills");
            Element<SearchBar>("life-search").Text = "English";
            await Click("life-search-go");
            var english = IssuedElements(Current()).OfType<Button>().Single(b => b.Text == "English");
            await Click(english.AutomationId);
            Element<Switch>("life-native-language").IsToggled = true;
            await Click("life-use-skill");
            await Back();
            await Click("life-open-resources");
            Element<Entry>("life-resource-investment").Text = "0";
            await Click("life-use-resources");
            await Back();
            await Click("life-open-gear"); await Click("life-begin-gear"); await Back();
            await Click("life-open-lifestyles"); await Click("life-begin-lifestyles"); await Back();
            await Click("life-open-contacts"); await Click("life-begin-contacts");
            await Click("life-add-contact");
            Element<Entry>("life-contact-name").Text = "Mara";
            Element<Entry>("life-contact-role").Text = "Fixer";
            await Click("life-use-contact"); await Back();
            await Click("life-open-magic"); await Click("life-begin-magic"); await Back();
            Require(new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!.ContentRevision == before.ContentRevision
                && probe!.ConfirmCalls == 0, "Draft phone inputs changed the runner before final confirmation.");
            string expected = JsonSerializer.Serialize(Session().Input);
            var retainedInput = Session().Input!;
            IssuedPageLifecycle(Current(), "OnDisappearing");
            string ownerKey = runtime.Coordinator.State.DisplayOwnerContext!.Value.Owner.Value;
            var staleInput = retainedInput with { Binding = retainedInput.Binding with { ContentRevision = retainedInput.Binding.ContentRevision + 1 } };
            await drafts.SaveAsync(ownerKey, staleInput, default);
            var staleSession = new LifeModuleCompletionSession(runtime.Coordinator, drafts);
            await staleSession.OpenAsync(default, () => true);
            Require(staleSession.Halted && staleSession.CanDiscardInputDraft && !staleSession.CanConfirm
                && JsonSerializer.Serialize(await drafts.LoadAsync(ownerKey, id, default)) == JsonSerializer.Serialize(staleInput),
                "Stale persisted choices were silently rebound or overwritten.");
            await staleSession.DiscardInputDraftAsync(default, () => true);
            Require(staleSession.Input?.TalentSelection is null && staleSession.Ready
                && new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!.ContentRevision == before.ContentRevision,
                "Explicit input discard altered modules/runner or failed to reacquire review.");
            // Restore the player's original test choices, not a cached preview or write permission.
            await drafts.SaveAsync(ownerKey, retainedInput, default);
            // A fresh page/session/store reads only inputs and obtains a new Core review.
            // This is managed cold reopen, not Android force-stop evidence.
            var reopened = new LifeModuleCompletionPage(runtime.Coordinator, store: new(runtime.StateDirectory));
            await navigation.PushAsync(reopened, false); await Appear();
            Require(JsonSerializer.Serialize(Session().Input) == expected && Session().Reviewed,
                "Cold phone reopen lost exact choices or reused old review authority.");
            Require(await drafts.LoadAsync("different-owner", id, default) is null, "Input storage crossed owner namespaces.");
            await Click("life-open-review");
            Element<Entry>("life-starting-dice").Text = "6";
            await Click("life-review-completion");
            Require(Session().CanConfirm, "Phone-selected Life Modules runner is blocked: " + string.Join(", ", Session().Blockers));
            Require(!Element<Button>("life-confirm-completion").IsEnabled, "Completion lacks explicit user acknowledgement.");
            Element<Switch>("life-completion-confirmed").IsToggled = true;
            var oldConfirm = Element<Button>("life-confirm-completion");
            Element<Entry>("life-starting-dice").Text = "5";
            await ui.BeginAsyncVoid(() => ((IButtonController)oldConfirm).SendClicked());
            Require(probe!.ConfirmCalls == 0, "Changed visible dice accepted the previous final review.");
            Element<Entry>("life-starting-dice").Text = "6";
            await Click("life-review-completion");
            Element<Switch>("life-completion-confirmed").IsToggled = true;
            await Click("life-confirm-completion");
            Require(Element<Label>("life-completion-saved").Text == CreationKarmaCopy.CareerReady && probe!.ConfirmCalls == 1,
                "Phone confirmation did not reopen the exact Career runner.");
            var cold = new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!;
            Require(cold.ContentRevision == before.ContentRevision + 1 && cold.SavedRevision == cold.ContentRevision
                && cold.Document.AuxiliaryState.CharacterCreationFinalizationArchive is not null,
                "Phone completion was not one durable Career transition.");
            IssuedPageLifecycle(Current(), "OnDisappearing");
            ui.AssertHealthy();
            Console.WriteLine("PASS Life Modules phone pages: explicit choices, stale controls/dice, input save/cold reopen, explicit stale-input recovery, owner separation, review and Career");

            NativePageBase Current() => (NativePageBase)navigation.Navigation.NavigationStack.Last();
            LifeModuleCompletionSession Session() => (LifeModuleCompletionSession)typeof(LifeModuleCompletionPage)
                .GetField("_session", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(Current())!;
            T Element<T>(string key) where T : Element => IssuedElements(Current()).OfType<T>().Single(e => e.AutomationId == key);
            async Task Appear()
            { var page = Current(); if (IssuedPageField<int>(page, "_subscribed") == 0) await ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing")); }
            async Task Click(string key)
            {
                var previous = Current(); var button = Element<Button>(key);
                Require(button.IsEnabled, "Disabled Life Modules action: " + key);
                await ui.BeginAsyncVoid(() => ((IButtonController)button).SendClicked());
                if (!ReferenceEquals(previous, Current()) && IssuedPageField<int>(previous, "_subscribed") != 0) IssuedPageLifecycle(previous, "OnDisappearing");
                await Appear();
            }
            async Task Back()
            { IssuedPageLifecycle(Current(), "OnDisappearing"); await navigation.PopAsync(false); await Appear(); }
        });
    }

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
