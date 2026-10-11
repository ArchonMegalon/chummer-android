using System.Globalization;
using Chummer.Android.Native;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Infrastructure.Workspaces;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    internal static async Task RunSkillsForwardAsync(string contentRoot)
    {
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            var culture = CultureInfo.CurrentUICulture;
            try
            {
                foreach (string scenario in new[] { "forward", "sum", "load-fault", "owner-b", "owner-aba", "departure", "return", "unknown" })
                {
                    CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(scenario == "sum" ? "de-AT" : scenario == "load-fault" ? "es-MX" : "en-GB");
                    var owners = new ControlledLinkedOwner();
                    SkillsForwardProbe? skills = null;
                    QualitiesForwardProbe? qualities = null;
                    await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners,
                        creationFinalization: true, creationAttributes: true, creationSkills: true,
                        productionCreationOverview: true, skillsDecorator: service => skills = new(service),
                        qualitiesDecorator: service => qualities = new(service));
                    var before = PrepareActualFinalizationReadyContext(runtime, stopBeforeSkills: true,
                        buildMethod: scenario == "sum" ? CharacterCreationBuildMethods.SumToTen : CharacterCreationBuildMethods.Priority);
                    var coordinator = runtime.Coordinator;
                    await HydrateFinalizationOwnerAsync(runtime, owners, before);
                    await coordinator.InitializeAsync();
                    await HydrateFinalizationOwnerAsync(runtime, owners, before);
                    var authority = coordinator.LoadCreationSkills().Value!;
                    var native = authority.Authority.KnowledgeSkills.First(x => x.CanBeNativeLanguage);
                    var running = CreationSkillsPhoneAuthority.AvailableActiveSkills(authority).Single(x => x.Name == "Running");
                    CharacterCreationSkillAllocation[] allocations =
                    [new(native.SourceSkillId, CharacterCreationSkillKinds.Knowledge, null, null, true),
                     new(running.SourceSkillId, CharacterCreationSkillKinds.Active, 1, null, false)];
                    var preview = coordinator.PreviewCreationSkills(authority.Binding, allocations, []).Value!;
                    Require(preview.CanConfirm, "SETUP: actual Core rejected legal Skills.");
                    var editor = new CreationSkillsPage(coordinator, authority);
                    var navigation = new NavigationPage(editor);
                    var review = new CreationSkillsPreviewPage(coordinator, preview, allocations, []);
                    await navigation.PushAsync(review, animated: false);
                    var window = new Window(navigation);
                    using var alerts = new IssuedPageAlerts(review, window);
                    await alerts.PreflightAsync();
                    await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(review, "OnAppearing")));
                    var save = MinimalVisible(review).OfType<Button>().Single(x => x.AutomationId == "creation-skills-confirm");
                    Require(save.IsEnabled, "SETUP: actual Skills review cannot be saved.");
                    Exception? observerFailure = null;
                    string openingText = CreationAllocationStrings.Get("SkillsPreview.OpeningQualities", "missing");
                    qualities!.BeforeLoad = () =>
                    {
                        Require(SynchronizationContext.Current != ui, "Next catalogue load blocked the UI thread.");
                        if (scenario == "load-fault") throw new IOException("synthetic-next-read-failure");
                        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        ui.Post(_ =>
                        {
                            try
                            {
                                Require(!save.IsEnabled && save.Text == openingText,
                                    "Saved Skills lacks visible pending continuation feedback.");
                                ((IButtonController)save).SendClicked();
                                MinimalRender(review);
                                if (scenario is "owner-b" or "owner-aba") owners.Set(ContactsOwnerB);
                                if (scenario == "owner-aba") owners.Set(ContactsOwnerA);
                                if (scenario is "departure" or "return") IssuedPageLifecycle(review, "OnDisappearing");
                                if (scenario == "return") IssuedPageLifecycle(review, "OnAppearing");
                                observed.SetResult();
                            }
                            catch (Exception error) { observerFailure = error; observed.SetException(error); }
                        }, null);
                        Require(observed.Task.Wait(TimeSpan.FromSeconds(5)), "Next catalogue load stopped the UI heartbeat.");
                        observed.Task.GetAwaiter().GetResult();
                    };
                    if (scenario == "unknown") skills!.AfterConfirm = () => throw new IOException("synthetic-confirm-result-loss");
                    await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((IButtonController)save).SendClicked()));
                    if (observerFailure is not null) throw observerFailure;
                    var saved = new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!;
                    Require(skills!.ConfirmCalls == 1 && saved.ContentRevision == before.ContentRevision + 1
                        && saved.SavedRevision == saved.ContentRevision, "Skills was not saved exactly once.");
                    bool advanced = scenario is "forward" or "sum";
                    if (advanced)
                    {
                        Require(navigation.Navigation.NavigationStack.Last() is CreationQualitiesPage
                            && !navigation.Navigation.NavigationStack.Contains(review),
                            "Saving Skills stranded the user on its receipt instead of opening Qualities: " + scenario
                            + "; loads=" + qualities.LoadCalls + "; result=" + qualities.LastResult?.Outcome
                            + "; blockers=" + string.Join(",", qualities.LastResult?.Blockers ?? []));
                        qualities.BeforeLoad = null;
                        int loads = qualities.LoadCalls;
                        var next = (CreationQualitiesPage)navigation.Navigation.NavigationStack.Last();
                        await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(next, "OnAppearing")));
                        Require(qualities.LoadCalls == loads && MinimalVisible(next).OfType<Button>().Any(x => x.AutomationId == "creation-qualities-open-review"),
                            "The handed-off catalogue was loaded twice or did not become usable.");
                        // A later appearance must not reuse that one-use handoff.
                        IssuedPageLifecycle(next, "OnDisappearing");
                        await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(next, "OnAppearing")));
                        Require(qualities.LoadCalls == loads + 1, "A later appearance reused the initial catalogue without Core revalidation.");
                        IssuedPageLifecycle(next, "OnDisappearing");
                        await navigation.PopAsync(animated: false);
                        Require(ReferenceEquals(navigation.Navigation.NavigationStack.Last(), editor),
                            "Back returned to a consumed review instead of the Skills editor.");
                    }
                    else Require(ReferenceEquals(navigation.Navigation.NavigationStack.Last(), review),
                        "An uncertain, failed or departed continuation stole navigation.");
                    if (scenario == "unknown") Require(qualities.LoadCalls == 0, "An uncertain commit entered the next step.");
                    if (scenario == "load-fault")
                    {
                        Require(MinimalVisibleText(review).Contains(CreationAllocationStrings.Get("SkillsPreview.NextUnavailable", "missing"), StringComparison.Ordinal),
                            "Read failure hid the saved Skills and read-only recovery action.");
                        qualities.BeforeLoad = null;
                        var retry = MinimalVisible(review).OfType<Button>().Single(x => x.AutomationId == "creation-skills-continue-qualities");
                        await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((IButtonController)retry).SendClicked()));
                        Require(navigation.Navigation.NavigationStack.Last() is CreationQualitiesPage && qualities.LoadCalls == 2,
                            "Continue did not retry only the catalogue read.");
                    }
                    Require(!save.IsEnabled, "A retired Skills save was re-enabled.");
                    ((IButtonController)save).SendClicked();
                    Require(skills.ConfirmCalls == 1 && qualities.ConfirmCalls == 0 && qualities.PreviewCalls == 0 && alerts.Titles.Count == 0,
                        "Forward/retry replayed a mutation or exposed an unhandled error.");
                    RequireSameRewardDocument(saved, new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!);
                    qualities.BeforeLoad = null;
                    if (advanced)
                    {
                        await HydrateFinalizationOwnerAsync(runtime, owners, saved);
                        var reopened = coordinator.LoadCreationSkills().Value!;
                        Require(reopened.Skills.Single(x => x.SourceSkillId == running.SourceSkillId).Rating == 1
                            && reopened.Skills.Single(x => x.SourceSkillId == native.SourceSkillId).IsNativeLanguage,
                            "Cold-store reopen lost actual saved Skills.");
                        RequireSameRewardDocument(saved, new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!);
                    }
                    IssuedPageLifecycle(review, "OnDisappearing");
                    Console.WriteLine("PASS Skills forward: " + scenario);
                }
            }
            finally { CultureInfo.CurrentUICulture = culture; }
        });
    }

    private sealed class QualitiesForwardProbe(IOwnerBoundCharacterCreationQualitiesService inner) : IOwnerBoundCharacterCreationQualitiesService
    {
        public Action? BeforeLoad;
        public int LoadCalls, PreviewCalls, ConfirmCalls;
        public CharacterCreationFoundationResult<CharacterCreationQualitiesState>? LastResult;
        public CharacterCreationFoundationResult<CharacterCreationQualitiesState> Load(OwnerContextStamp owner, CharacterCreationQualitiesLoadRequest request)
        { LoadCalls++; BeforeLoad?.Invoke(); return LastResult = inner.Load(owner, request); }
        public CharacterCreationFoundationResult<CharacterCreationQualitiesPreview> Preview(OwnerContextStamp owner, CharacterCreationQualitiesPreviewRequest request)
        { PreviewCalls++; return inner.Preview(owner, request); }
        public CharacterCreationFoundationResult<CharacterCreationQualitiesDraftReceipt> Confirm(OwnerContextStamp owner, CharacterCreationQualitiesConfirmRequest request)
        { ConfirmCalls++; return inner.Confirm(owner, request); }
    }
}
