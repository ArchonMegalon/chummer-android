using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Chummer.Android.Native;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    public static async Task RunMagicReReviewCasesAsync(string contentRoot, string sourceDirectory,
        ICharacterSourceDataResolver resolver, CharacterWorkspaceId id)
    {
        foreach (string locale in new[] { "de-AT", "en-GB", "es-MX" })
            foreach (string key in new[] { "Title", "Intro", "Binding", "Confirm", "ConfirmBody", "Saving", "Saved", "Unavailable" })
                Require(CreationAllocationStrings.Get("MagicReReview." + key, "missing", CultureInfo.GetCultureInfo(locale)) != "missing",
                    "Missing Magic re-review satellite resource: " + locale + "/" + key);
        foreach (var (linked, failure) in new[] { (false, "none"), (true, "none"), (true, "read"), (true, "reply") })
        {
            var owner = linked ? ContactsOwnerA : OwnerScope.LocalSingleUser;
            var owners = new ControlledLinkedOwner();
            owners.Set(owner);
            MagicReviewFailure? probe = null;
            await using var runtime = new NativeRewardRuntime(contentRoot, productionCreationOverview: true,
                linkedOwners: owners, creationSkillsSeed: target => SeedMagicHistory(target, sourceDirectory, resolver, id, owner),
                magicDecorator: inner => probe = new(inner, failure));
            runtime.Id = id;
            await runtime.Presenter.LoadAsync(id, default);
            var store = new FileWorkspaceStore(runtime.StateDirectory);
            WorkspaceStoredDocument Read() => (linked ? store.Get(owner, id) : store.Get(id)).Value!;
            var before = Read();
            string initial = JsonSerializer.Serialize(before);
            Require(!linked || !store.Get(id).Success, "Legacy copy masked an owner-partition defect.");
            if (!linked) await MagicReReviewBudgetEntryAsync(runtime.Coordinator);
            Require(runtime.Coordinator.LoadCreationMagicResonance().Value is null,
                "Ordinary stale Magic editing became available.");
            var loaded = runtime.Coordinator.LoadCreationMagicReReview();
            Require(loaded.Value is not null, "Native re-review load: " + string.Join(",", loaded.Blockers));
            var state = loaded.Value!;
            Require(!CreationMagicReReviewPhoneAuthority.Matches(state with { SnapshotDigest = "invented" }, runtime.Coordinator.State),
                "Invented review hash was accepted.");
            Require((await runtime.Coordinator.ConfirmCreationMagicReReviewAsync(state, false)).Receipt is null
                && JsonSerializer.Serialize(Read()) == initial, "Review saved without explicit confirmation.");
            var page = new CreationMagicReReviewPage(runtime.Coordinator, state);
            void Render() => typeof(CreationMagicReReviewPage).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null);
            Render();
            Button Confirm() => ((VerticalStackLayout)((ScrollView)page.Content!).Content!).Children.OfType<Button>()
                .Single(button => button.AutomationId == "creation-magic-rereview-confirm");
            Require(!Confirm().IsEnabled, "Detached review enabled a confirmation.");
            typeof(CreationMagicReReviewPage).GetField("_attached", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, true);
            Render();
            Require(Confirm().IsEnabled && ((VerticalStackLayout)((ScrollView)page.Content!).Content!).Children.OfType<Label>()
                .Any(label => label.Text.Contains("Light Body", StringComparison.Ordinal)), "Native review omitted saved source choices.");
            Require(!MinimalVisible(page).OfType<Label>().Any(label => label.AutomationId == "creation-magic-rereview-binding"),
                "Internal Attributes revision is visible before expanding review details.");
            var detailsToggle = MinimalVisible(page).OfType<Button>()
                .Single(button => button.AutomationId == "creation-magic-rereview-details-toggle");
            ((IButtonController)detailsToggle).SendClicked();
            Require(MinimalVisible(page).OfType<Label>().Any(label => label.AutomationId == "creation-magic-rereview-binding"),
                "Explicit re-review troubleshooting lost the exact binding.");
            ((IButtonController)detailsToggle).SendClicked();
            Require(JsonSerializer.Serialize(Read()) == initial, "Review disclosure changed the runner.");

            if (linked && failure == "none")
            {
                owners.Set(ContactsOwnerB);
                typeof(CreationMagicReReviewPage).GetField("_saving", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, true);
                Render();
                Require(!((VerticalStackLayout)((ScrollView)page.Content!).Content!).Children.OfType<Label>()
                    .Any(label => label.Text.Contains("Light Body", StringComparison.Ordinal)),
                    "Saving feedback retained another account's private choices.");
                typeof(CreationMagicReReviewPage).GetField("_saving", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, false);
                owners.Set(owner);
                Require(!runtime.Coordinator.IsCreationMagicReReviewCurrent(state)
                    && (await runtime.Coordinator.ConfirmCreationMagicReReviewAsync(state, true)).Receipt is null
                    && probe!.ConfirmCalls == 0 && JsonSerializer.Serialize(Read()) == initial,
                    "Old owner epoch retained mutation permission after A→B→A.");
                await HydrateFinalizationOwnerAsync(runtime, owners, before);
                state = runtime.Coordinator.LoadCreationMagicReReview().Value
                    ?? throw new InvalidOperationException("Fresh owner-bound reopen did not recover review.");
            }
            var result = await runtime.Coordinator.ConfirmCreationMagicReReviewAsync(state, true);
            var after = Read();
            Require(probe!.ConfirmCalls == 1 && after.ContentRevision == before.ContentRevision + 1
                && after.SavedRevision == after.ContentRevision && after.Document.Content == before.Document.Content
                && JsonSerializer.Serialize(after.Document.AuxiliaryState.CharacterCreationMagicResonanceDraft!.Selections)
                    == JsonSerializer.Serialize(before.Document.AuxiliaryState.CharacterCreationMagicResonanceDraft!.Selections),
                "Actual owner-bound re-review failed to preserve choices/XML and checkpoint exactly once.");
            Require((await runtime.Coordinator.ConfirmCreationMagicReReviewAsync(state, true)).Receipt is null
                && probe.ConfirmCalls == 1, "A retained review replayed a known/uncertain write.");
            if (failure == "reply")
                Require(result.Outcome == "outcome-unknown" && result.Receipt is null,
                    "Lost commit reply was reported as a known save.");
            else
            {
                var receipt = result.Receipt ?? throw new InvalidOperationException("Known save receipt was lost.");
                Require(runtime.Coordinator.CanDisplayCreationMagicReReviewReceipt(receipt),
                    "Known save receipt was lost after confirmation/refresh.");
                Require((result.Blockers.Count == 0) == (failure == "none"), "Post-commit read failure was hidden.");
                var savedPage = new CreationMagicReReviewPage(runtime.Coordinator, state);
                typeof(CreationMagicReReviewPage).GetField("_confirmation", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .SetValue(savedPage, result);
                MinimalRender(savedPage);
                MinimalRequireNoMachineValues(savedPage);
                var receiptToggle = MinimalVisible(savedPage).OfType<Button>()
                    .Single(button => button.AutomationId == "creation-magic-rereview-receipt-details-toggle");
                ((IButtonController)receiptToggle).SendClicked();
                Require(MinimalVisibleText(savedPage).Contains(receipt.ReceiptDigest),
                    "Expanded re-review receipt lost its exact identity.");
                ((IButtonController)receiptToggle).SendClicked();
                MinimalRequireNoMachineValues(savedPage);
                owners.Set(ContactsOwnerB);
                Require(!runtime.Coordinator.CanDisplayCreationMagicReReviewReceipt(receipt),
                    "A different account could display the save receipt.");
                MinimalRender(savedPage);
                Require(!MinimalVisible(savedPage).OfType<Button>().Any(button =>
                    button.AutomationId == "creation-magic-rereview-receipt-details-toggle"),
                    "A different account retained the old receipt disclosure.");
            }
            var coldOwner = new ControlledLinkedOwner();
            coldOwner.Set(owner);
            var cold = new OwnerBoundCharacterCreationMagicResonanceService(new FileWorkspaceStore(runtime.StateDirectory), coldOwner, resolver);
            var coldState = cold.Load(coldOwner.Capture(), new(id));
            Require(coldState.Value is { CanEdit: true }, "Cold owner-bound Magic did not reopen after re-review.");
        }
        Console.WriteLine("PASS actual native Magic re-review: local/linked, owner ABA, one save, cold reopen, refresh failure and uncertain reply/no replay");
    }

    private static void SeedMagicHistory(string target, string sourceDirectory, ICharacterSourceDataResolver resolver,
        CharacterWorkspaceId id, OwnerScope owner)
    {
        var source = new FileWorkspaceStore(sourceDirectory).Get(id).Value!;
        var store = new FileWorkspaceStore(target);
        var empty = new WorkspaceDocument(source.Document.Content, source.Document.RulesetId, source.Document.Format);
        Require((owner.IsLocalSingleUser ? store.CreateWorkspaceDocument(id, empty) : store.CreateWorkspaceDocument(owner, id, empty)).Success,
            "Could not create isolated fixture partition.");
        string directory = (string)typeof(FileWorkspaceStore).GetMethod("GetWorkspaceDirectory", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(store, [owner])!;
        File.Copy(Directory.GetFiles(sourceDirectory, id.Value + ".json", SearchOption.AllDirectories).Single(),
            Path.Combine(directory, id.Value + ".json"), overwrite: true); // This test fixture only.
        var owners = new ControlledLinkedOwner();
        owners.Set(owner);
        var attributes = new OwnerBoundCharacterCreationAttributesService(store, owners, resolver);
        var current = attributes.Load(owners.Capture(), new(id)).Value!;
        var choices = current.PendingDraft!.Allocations.Select(row => row.AttributeId == "BOD" ? row with { PriorityPoints = 1 } : row).ToArray();
        var preview = attributes.Preview(owners.Capture(), new(current.Binding, choices)).Value!;
        Require(preview.CanConfirm && attributes.Confirm(owners.Capture(), new(preview.Binding, choices, preview.PreviewDigest, true)).Value is not null,
            "Actual later BOD decision did not create the stale Magic fixture.");
    }

    private static async Task MagicReReviewBudgetEntryAsync(RunnerSessionCoordinator coordinator)
    {
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            var page = new BuildPage(coordinator);
            var navigation = new NavigationPage(page);
            _ = new Window(navigation);
            using var lifetime = new CancellationTokenSource();
            typeof(BuildPage).GetField("_creationDashboardRouteReadyLifetime", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(page, lifetime);
            typeof(BuildPage).GetField("_creationDashboardAppearanceGeneration", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(page, 1L);
            try
            {
                var snapshot = coordinator.State.CreationWizard!;
                snapshot = snapshot with
                {
                    Steps = snapshot.Steps.Where(row => row.StepId == CharacterCreationWizardStepIds.MagicResonance).ToArray(),
                    Budgets = snapshot.Budgets.Where(row => row.BudgetId == CharacterCreationBudgetIds.SpellsFormsPrograms).ToArray()
                };
                Require(snapshot.Steps.Count == 1 && snapshot.Budgets.Count == 1 && !snapshot.Budgets[0].IsExact,
                    "SETUP: stale Magic must have an inexact wizard budget.");
                var stages = typeof(BuildPage).GetMethod("AddWizardStages", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var pending = new CreationDashboardRenderReadiness(
                    () => false, () => false, () => false, () => false, () => false, () => false);
                var blocked = (IReadOnlyDictionary<string, CreationBudgetRoute>)stages.Invoke(page,
                    [snapshot, null, null, null, null, null, null, pending])!;
                Require(!blocked[CharacterCreationWizardStepIds.MagicResonance].CanOpen,
                    "Magic review opened without current Attributes readiness.");
                var readiness = new CreationDashboardRenderReadiness(
                    () => true, () => false, () => false, () => false, () => false, () => false);
                var routes = (IReadOnlyDictionary<string, CreationBudgetRoute>)stages.Invoke(page,
                    [snapshot, null, null, null, null, null, null, readiness])!;
                Require(routes[CharacterCreationWizardStepIds.MagicResonance].CanOpen && !readiness.MagicResonance,
                    "Magic recovery route did not open or fabricated ordinary readiness.");
                typeof(BuildPage).GetMethod("AddBudgetRibbon", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page,
                    [snapshot, null, null, readiness, routes, null, null]);
                var body = (VerticalStackLayout)((ScrollView)page.Content!).Content!;
                var card = body.Children.OfType<FlexLayout>().Single().Children.OfType<Border>().Single();
                var grid = (Grid)card.Content!;
                Require(grid.Children.OfType<VerticalStackLayout>().Single().Children.OfType<Label>().First()
                    .Text.EndsWith("Not exact", StringComparison.Ordinal), "Review link fabricated an exact Magic budget.");
                await ui.BeginAsyncVoid(() => ((IButtonController)grid.Children.OfType<Button>().Single()).SendClicked());
                Require(navigation.Navigation.NavigationStack.Last() is CreationMagicReReviewPage,
                    "Not exact Magic tap did not open the actual comparison page.");
                await navigation.PopAsync(false);
                ui.AssertHealthy();
            }
            finally { typeof(BuildPage).GetMethod("OnDisappearing", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null); }
        });
    }

    private sealed class MagicReviewFailure(IOwnerBoundCharacterCreationMagicResonanceService inner, string failure)
        : IOwnerBoundCharacterCreationMagicResonanceService, IOwnerBoundCharacterCreationMagicResonanceReReviewService
    {
        private readonly IOwnerBoundCharacterCreationMagicResonanceReReviewService _review = (IOwnerBoundCharacterCreationMagicResonanceReReviewService)inner;
        private bool _committed;
        public int ConfirmCalls { get; private set; }
        public CharacterCreationFoundationResult<CharacterCreationMagicResonanceState> Load(OwnerContextStamp owner, CharacterCreationMagicResonanceLoadRequest request)
        {
            if (_committed && failure == "read") throw new IOException("Injected postcommit read failure");
            return inner.Load(owner, request);
        }
        public CharacterCreationFoundationResult<CharacterCreationMagicResonancePreview> Preview(OwnerContextStamp owner, CharacterCreationMagicResonancePreviewRequest request) => inner.Preview(owner, request);
        public CharacterCreationFoundationResult<CharacterCreationMagicResonanceReceipt> Confirm(OwnerContextStamp owner, CharacterCreationMagicResonanceConfirmRequest request) => inner.Confirm(owner, request);
        public CharacterCreationFoundationResult<CharacterCreationMagicResonanceReReviewState> LoadReReview(OwnerContextStamp owner, CharacterCreationMagicResonanceLoadRequest request) => _review.LoadReReview(owner, request);
        public CharacterCreationFoundationResult<CharacterCreationMagicResonanceReReviewPreview> PreviewReReview(OwnerContextStamp owner, CharacterCreationMagicResonanceReReviewPreviewRequest request) => _review.PreviewReReview(owner, request);
        public CharacterCreationFoundationResult<CharacterCreationMagicResonanceReceipt> ConfirmReReview(OwnerContextStamp owner, CharacterCreationMagicResonanceReReviewConfirmRequest request)
        {
            ConfirmCalls++;
            var result = _review.ConfirmReReview(owner, request);
            _committed = result.Value is not null && result.Outcome == CharacterCreationFoundationOutcomes.Success;
            if (_committed && failure == "reply") throw new IOException("Injected lost commit reply");
            return result;
        }
    }
}
