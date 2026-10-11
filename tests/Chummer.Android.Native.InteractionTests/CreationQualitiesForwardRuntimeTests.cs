using System.Globalization;
using Chummer.Android.Native;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Workspaces;
using Chummer.Presentation.Overview;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    internal static async Task RunQualitiesForwardAsync(string contentRoot)
    {
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            var culture = CultureInfo.CurrentUICulture;
            try
            {
                foreach (string scenario in new[] { "mundane", "sum", "magician", "load-fault",
                    "owner-b", "owner-aba", "departure", "return", "unknown", "cold-receipt" })
                {
                    CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(
                        scenario == "sum" ? "de-AT" : scenario == "load-fault" ? "es-MX" : "en-GB");
                    var owners = new ControlledLinkedOwner();
                    QualitiesForwardProbe? qualities = null;
                    QualitiesNextMagicProbe? magic = null;
                    QualitiesNextResourcesProbe? resources = null;
                    await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners,
                        creationFinalization: true, creationAttributes: true, creationSkills: true,
                        productionCreationOverview: true, qualitiesDecorator: service => qualities = new(service),
                        magicDecorator: service => magic = new(service),
                        resourcesDecorator: presenter => resources = new(presenter));
                    var before = PrepareActualFinalizationReadyContext(runtime, stopBeforeQualities: true,
                        buildMethod: scenario == "sum" ? CharacterCreationBuildMethods.SumToTen : CharacterCreationBuildMethods.Priority,
                        attributeTalent: scenario == "magician" ? CharacterCreationMagicResonanceKinds.Magician : null,
                        attributeTalentRank: "A");
                    var coordinator = runtime.Coordinator;
                    await HydrateFinalizationOwnerAsync(runtime, owners, before);
                    await coordinator.InitializeAsync();
                    await HydrateFinalizationOwnerAsync(runtime, owners, before);
                    var original = coordinator.State;
                    var authority = coordinator.LoadCreationQualities().Value!;
                    var preview = coordinator.PreviewCreationQualities(authority.Binding, [], original).Value!;
                    Require(preview.CanConfirm, "SETUP: actual Core rejected the empty legal Qualities draft.");
                    var backend = new QualitiesFeedbackBackend();
                    var journal = new CharacterCreationQualitiesCheckpointStore(backend);
                    var reviewed = CharacterCreationQualitiesCheckpoint.CreateReviewed(preview, [], Guid.NewGuid());
                    Require(journal.TryCreate(reviewed, out reviewed, out var blocker), blocker);
                    var editor = new CreationQualitiesPage(coordinator, journal);
                    var navigation = new NavigationPage(editor);
                    var review = new CreationQualitiesReviewPage(coordinator, reviewed, journal, original);
                    await navigation.PushAsync(review, animated: false);
                    var window = new Window(navigation);
                    using var alerts = new IssuedPageAlerts(review, window);
                    await alerts.PreflightAsync();
                    await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(review, "OnAppearing")));
                    var save = MinimalVisible(review).OfType<Button>().Single(x => x.AutomationId == "creation-qualities-confirm-draft");
                    Require(save.IsEnabled, "SETUP: Qualities review is disabled.");
                    Exception? observationFailure = null;
                    string openingText = CreationFlowStrings.Get("Qualities.Review.OpeningNext", "missing");
                    // This explicit load happens after confirmation; production overview
                    // hydration uses its own real Core reader, not this probe.
                    magic!.BeforeLoad = () =>
                    {
                        Require(SynchronizationContext.Current != ui, "Next-step preparation blocked the UI.");
                        if (scenario is "load-fault" or "cold-receipt") throw new IOException("synthetic-next-read-failure");
                        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        ui.Post(_ =>
                        {
                            try
                            {
                                Require(!save.IsEnabled && save.Text == openingText,
                                    "Saved Qualities lacks visible continuation feedback: " + save.Text + " expected " + openingText);
                                ((IButtonController)save).SendClicked();
                                if (scenario is "owner-b" or "owner-aba") owners.Set(ContactsOwnerB);
                                if (scenario == "owner-aba") owners.Set(ContactsOwnerA);
                                if (scenario is "departure" or "return") IssuedPageLifecycle(review, "OnDisappearing");
                                if (scenario == "return") IssuedPageLifecycle(review, "OnAppearing");
                                observed.SetResult();
                            }
                            catch (Exception error) { observationFailure = error; observed.SetException(error); }
                        }, null);
                        Require(observed.Task.Wait(TimeSpan.FromSeconds(5)), "Next read prevented UI feedback.");
                        observed.Task.GetAwaiter().GetResult();
                    };
                    if (scenario == "unknown") qualities!.AfterConfirm = () => throw new IOException("synthetic-result-loss");
                    await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((IButtonController)save).SendClicked()));
                    if (observationFailure is not null) throw observationFailure;
                    magic.BeforeLoad = null;
                    var saved = new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!;
                    Require(qualities!.ConfirmCalls == 1 && saved.ContentRevision == before.ContentRevision + 1
                        && saved.SavedRevision == saved.ContentRevision, "Qualities did not save exactly once.");
                    bool advanced = scenario is "mundane" or "sum" or "magician";
                    if (advanced)
                    {
                        Require(navigation.Navigation.NavigationStack.Last() is CreationMagicResonancePage == (scenario == "magician")
                            && navigation.Navigation.NavigationStack.Last() is CreationResourcesPage == (scenario != "magician")
                            && !navigation.Navigation.NavigationStack.Contains(review),
                            "Qualities did not open the applicable next step: " + scenario);
                        Require(!journal.TryRead(out _, out _), "Verified forward navigation left the Applied claim locked.");
                        var next = (NativePageBase)navigation.Navigation.NavigationStack.Last();
                        int magicLoads = magic.LoadCalls, resourceLoads = resources!.LoadCalls;
                        await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(next, "OnAppearing")));
                        Require(magic.LoadCalls == magicLoads && resources.LoadCalls == resourceLoads,
                            "Next-page appearance discarded its exact one-use read.");
                        Require(MinimalVisible(next).OfType<Button>().Any(x => x.IsEnabled),
                            "Next page rendered no usable action.");
                        IssuedPageLifecycle(next, "OnDisappearing");
                        await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(next, "OnAppearing")));
                        Require(scenario == "magician" ? magic.LoadCalls == magicLoads + 1
                            : resources.LoadCalls == resourceLoads + 1, "Re-entry reused a consumed next-step read.");
                        IssuedPageLifecycle(next, "OnDisappearing");
                        await navigation.PopAsync(animated: false);
                        Require(ReferenceEquals(navigation.Navigation.NavigationStack.Last(), editor),
                            "Back returned to the consumed Qualities review.");
                    }
                    else
                    {
                        Require(ReferenceEquals(navigation.Navigation.NavigationStack.Last(), review),
                            "Failed, stale or uncertain continuation stole navigation.");
                        Require(journal.TryRead(out var retained, out _)
                            && retained.Phase == (scenario == "unknown"
                                ? CharacterCreationQualitiesCheckpointPhase.Applying : CharacterCreationQualitiesCheckpointPhase.Applied),
                            "Failed continuation discarded a recovery claim.");
                    }
                    if (scenario is "load-fault" or "cold-receipt")
                    {
                        Require(MinimalVisibleText(review).Contains(CreationFlowStrings.Get("Qualities.Review.NextUnavailable", "missing")),
                            "A next-read failure hid the successful save.");
                        if (scenario == "cold-receipt")
                        {
                            IssuedPageLifecycle(review, "OnDisappearing");
                            await HydrateFinalizationOwnerAsync(runtime, owners, saved);
                            var reopenedJournal = new CharacterCreationQualitiesCheckpointStore(backend);
                            Require(reopenedJournal.TryRead(out var applied, out _), "Cold journal lost the saved claim.");
                            var receiptPage = new CreationQualitiesReceiptPage(coordinator, applied, applied.Receipt!, reopenedJournal);
                            await navigation.PushAsync(receiptPage, animated: false);
                            await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(receiptPage, "OnAppearing")));
                            var resume = MinimalVisible(receiptPage).OfType<Button>().Single(x => x.AutomationId == "creation-qualities-receipt-acknowledge");
                            await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((IButtonController)resume).SendClicked()));
                        }
                        else
                        {
                            var retry = MinimalVisible(review).OfType<Button>().Single(x => x.AutomationId == "creation-qualities-continue-next");
                            Require(retry.IsEnabled, "Saved review retained a disabled read-only Continue button.");
                            await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((IButtonController)retry).SendClicked()));
                        }
                        Require(navigation.Navigation.NavigationStack.Last() is CreationResourcesPage
                            && !journal.TryRead(out _, out _), "Read-only retry/recovery did not advance.");
                    }
                    if (scenario == "unknown") Require(magic.LoadCalls == 0, "Uncertain save advanced without receipt.");
                    ((IButtonController)save).SendClicked();
                    Require(qualities.ConfirmCalls == 1 && resources!.ConfirmCalls == 0
                        && magic.ConfirmCalls == 0 && alerts.Titles.Count == 0,
                        "Continuation replayed a mutation or exposed a raw error.");
                    RequireSameRewardDocument(saved, new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!);
                    IssuedPageLifecycle(review, "OnDisappearing");
                    Console.WriteLine("PASS Qualities forward: " + scenario);
                }
            }
            finally { CultureInfo.CurrentUICulture = culture; }
        });
    }

    private sealed class QualitiesNextMagicProbe(IOwnerBoundCharacterCreationMagicResonanceService inner)
        : IOwnerBoundCharacterCreationMagicResonanceService
    {
        public Action? BeforeLoad;
        public int LoadCalls, ConfirmCalls;
        public CharacterCreationFoundationResult<CharacterCreationMagicResonanceState> Load(OwnerContextStamp owner, CharacterCreationMagicResonanceLoadRequest request)
        { LoadCalls++; BeforeLoad?.Invoke(); return inner.Load(owner, request); }
        public CharacterCreationFoundationResult<CharacterCreationMagicResonancePreview> Preview(OwnerContextStamp owner, CharacterCreationMagicResonancePreviewRequest request)
            => inner.Preview(owner, request);
        public CharacterCreationFoundationResult<CharacterCreationMagicResonanceReceipt> Confirm(OwnerContextStamp owner, CharacterCreationMagicResonanceConfirmRequest request)
        { ConfirmCalls++; return inner.Confirm(owner, request); }
    }

    private sealed class QualitiesNextResourcesProbe(ICharacterCreationResourcesInteractionPresenter inner)
        : ICharacterCreationResourcesInteractionPresenter
    {
        public int LoadCalls, ConfirmCalls;
        public CharacterCreationResourcesInteractionLoadResult Load(CharacterOverviewState display)
        { LoadCalls++; return inner.Load(display); }
        public CharacterCreationResourcesInteractionPrepareResult Prepare(CharacterOverviewState display, string optionId)
            => inner.Prepare(display, optionId);
        public CharacterCreationResourcesInteractionConfirmResult Confirm(CharacterOverviewState display, CharacterCreationResourcesConfirmation confirmation)
        { ConfirmCalls++; return inner.Confirm(display, confirmation); }
        public CharacterCreationResourcesInteractionReceiptLookupResult LookupReceipt(CharacterOverviewState display, string idempotencyKey)
            => inner.LookupReceipt(display, idempotencyKey);
    }
}
