using System.Globalization;
using System.Text.Json;
using Chummer.Android.Native;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Workspaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    internal static async Task RunQualitiesAfterAttributesAsync(string contentRoot)
    {
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            foreach (string locale in new[] { "en-GB", "de-AT", "es-MX" })
                foreach (string key in new[] { "Title", "Body", "None", "Keep", "Cancel", "Reopen" })
                    Require(CreationAllocationStrings.Get("QualitiesReReview." + key, "missing",
                        CultureInfo.GetCultureInfo(locale)) != "missing", "Missing recovery translation.");
            foreach (string scenario in new[] { "keep", "cancel", "owner-aba", "departure", "unknown" })
            {
                var owners = new ControlledLinkedOwner();
                QualitiesAttributeReviewProbe? probe = null;
                await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners,
                    creationFinalization: true, creationAttributes: true, creationSkills: true,
                    productionCreationOverview: true, qualitiesDecorator: service => probe = new(service));
                var historical = PrepareActualFinalizationReadyContext(runtime, stopBeforeResources: true);
                var attributes = runtime.Services.GetRequiredService<ICharacterCreationAttributesService>();
                CharacterCreationAttributeAllocation[] allocations = [new("BOD", 1, 0)];
                var attributePreview = attributes.Preview(new(attributes.Load(new(runtime.Id)).Value!.Binding, allocations)).Value!;
                Require(attributes.Confirm(new(attributePreview.Binding, allocations, attributePreview.PreviewDigest, true)).Value is not null,
                    "SETUP: actual attribute edit failed.");
                // Reproduce the real route: Attributes changed, then the existing
                // explicit Skills re-review saved, leaving older Qualities next.
                var skills = (ICharacterCreationSkillsReReviewService)runtime.Services.GetRequiredService<ICharacterCreationSkillsService>();
                var skillReview = skills.LoadReReview(new(runtime.Id)).Value
                    ?? throw new InvalidOperationException("SETUP: actual Skills re-review unavailable.");
                Require(skills.ConfirmReReview(new(skillReview.Binding, skillReview.HistoricalDraft.Allocations,
                    skillReview.HistoricalDraft.GroupAllocations, skillReview.InitialPreview.PreviewDigest,
                    "quality-forward-skills-review", true, true)).Value is not null, "SETUP: actual Skills review failed.");
                var before = new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!;
                await HydrateFinalizationOwnerAsync(runtime, owners, before);
                var coordinator = runtime.Coordinator;
                var state = coordinator.LoadCreationSkills().Value!;
                var page = new CreationSkillsPage(coordinator, state);
                var navigation = new NavigationPage(page);
                var window = new Window(navigation);
                using var alerts = new IssuedPageAlerts(page, window, args =>
                {
                    Require(args.Title == CreationAllocationStrings.Get("QualitiesReReview.Title", "missing"),
                        "Unexpected confirmation instead of the focused retained-choice question.");
                    if (scenario == "owner-aba") { var owner = owners.Current; owners.Set(ContactsOwnerB); owners.Set(owner); }
                    if (scenario == "departure") IssuedPageLifecycle(page, "OnDisappearing");
                    args.SetResult(scenario != "cancel");
                });
                await alerts.PreflightAsync();
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing")));
                int uiThread = Environment.CurrentManagedThreadId;
                probe!.BeforeReReview = () => Require(Environment.CurrentManagedThreadId != uiThread,
                    "Qualities recovery read or save blocked the UI thread.");
                if (scenario == "unknown") probe.AfterReReview = () => throw new IOException("synthetic-lost-save-result");
                var next = MinimalVisible(page).OfType<Button>().Single(x => x.AutomationId == "creation-skills-continue");
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() =>
                {
                    ((IButtonController)next).SendClicked();
                    ((IButtonController)next).SendClicked();
                }));
                var saved = new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!;
                bool committed = scenario is "keep" or "unknown";
                Require(probe.ConfirmCalls == (committed ? 1 : 0) && probe.OrdinaryConfirmCalls == 0,
                    "Recovery bypassed its explicit seam or dispatched twice.");
                Require(alerts.Titles.Count == 1, "Continue duplicated or skipped the recovery question.");
                if (!committed) RequireSameRewardDocument(before, saved);
                else
                {
                    Require(saved.ContentRevision == before.ContentRevision + 1 && saved.SavedRevision == saved.ContentRevision,
                        "Retained Qualities did not persist exactly once.");
                    var draft = saved.Document.AuxiliaryState.CharacterCreationQualitiesDraft!;
                    Require(JsonSerializer.Serialize(draft.Selections) == JsonSerializer.Serialize(
                        historical.Document.AuxiliaryState.CharacterCreationQualitiesDraft!.Selections), "Saved selections changed.");
                    Require(draft.AttributesDraftDigest == saved.Document.AuxiliaryState.CharacterCreationAttributesDraft!.DraftDigest,
                        "Saved Qualities still use the old Attributes.");
                    var expected = before.Document.AuxiliaryState with
                    {
                        CharacterCreationQualitiesDraft = draft,
                        CharacterCreationQualitiesReceipts = saved.Document.AuxiliaryState.CharacterCreationQualitiesReceipts
                    };
                    Require(JsonSerializer.Serialize(expected) == JsonSerializer.Serialize(saved.Document.AuxiliaryState),
                        "Recovery changed an unrelated pending decision.");
                }
                Require((navigation.Navigation.NavigationStack.Last() is CreationQualitiesPage) == (scenario == "keep"),
                    "Recovery did not advance exactly when saved and still current.");
                if (scenario == "unknown")
                {
                    Require(!next.IsEnabled && MinimalVisibleText(page).Contains(
                        CreationAllocationStrings.Get("QualitiesReReview.Reopen", "missing")), "Uncertain save advertised blind retry.");
                    // MAUI suppresses Clicked on a disabled button; there must
                    // be no async page operation for the harness to join.
                    ((IButtonController)next).SendClicked();
                    Require(probe.ConfirmCalls == 1, "Uncertain recovery repeated the mutation.");
                }
                if (committed)
                {
                    IssuedPageLifecycle(page, "OnDisappearing");
                    await HydrateFinalizationOwnerAsync(runtime, owners, saved);
                    Require(await coordinator.LoadCreationQualitiesReReviewAsync(coordinator.State) is null,
                        "A fresh load offered a second recovery for the committed draft.");
                    Require(CreationQualitiesPhoneAuthority.IsReady(coordinator.LoadCreationQualities().Value!, coordinator.State),
                        "Reopened saved Qualities did not enable normal editing.");
                    RequireSameRewardDocument(saved, new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!);
                }
                IssuedPageLifecycle(page, "OnDisappearing");
                Console.WriteLine("PASS retained Qualities after Attributes: " + scenario);
            }
        });
    }

    private sealed class QualitiesAttributeReviewProbe(IOwnerBoundCharacterCreationQualitiesService inner)
        : IOwnerBoundCharacterCreationQualitiesService, IOwnerBoundCharacterCreationQualitiesReReviewService
    {
        public Action? BeforeReReview, AfterReReview;
        public int ConfirmCalls, OrdinaryConfirmCalls;
        public CharacterCreationFoundationResult<CharacterCreationQualitiesState> Load(OwnerContextStamp owner, CharacterCreationQualitiesLoadRequest request)
            => inner.Load(owner, request);
        public CharacterCreationFoundationResult<CharacterCreationQualitiesPreview> Preview(OwnerContextStamp owner, CharacterCreationQualitiesPreviewRequest request)
            => inner.Preview(owner, request);
        public CharacterCreationFoundationResult<CharacterCreationQualitiesDraftReceipt> Confirm(OwnerContextStamp owner, CharacterCreationQualitiesConfirmRequest request)
        { OrdinaryConfirmCalls++; return inner.Confirm(owner, request); }
        public CharacterCreationFoundationResult<CharacterCreationQualitiesReReviewState> LoadReReview(OwnerContextStamp owner, CharacterCreationQualitiesLoadRequest request)
        { BeforeReReview?.Invoke(); return ((IOwnerBoundCharacterCreationQualitiesReReviewService)inner).LoadReReview(owner, request); }
        public CharacterCreationFoundationResult<CharacterCreationQualitiesDraftReceipt> ConfirmReReview(OwnerContextStamp owner, CharacterCreationQualitiesReReviewConfirmRequest request)
        {
            BeforeReReview?.Invoke(); ConfirmCalls++;
            var result = ((IOwnerBoundCharacterCreationQualitiesReReviewService)inner).ConfirmReReview(owner, request);
            AfterReReview?.Invoke();
            return result;
        }
    }
}
