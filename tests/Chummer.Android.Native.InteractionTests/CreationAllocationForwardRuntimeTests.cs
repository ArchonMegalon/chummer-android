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
    internal static async Task RunAttributeForwardAsync(string contentRoot)
    {
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            var culture = CultureInfo.CurrentUICulture;
            try
            {
                foreach (string scenario in new[] { "forward", "sum", "awakened", "load-fault", "owner-b", "owner-aba", "departure", "return", "unknown" })
                {
                    CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(scenario == "sum" ? "de-AT" : scenario == "load-fault" ? "es-MX" : "en-GB");
                    var owners = new ControlledLinkedOwner();
                    AttributesCommitProbe? attributes = null;
                    SkillsForwardProbe? skills = null;
                    await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners,
                        creationFinalization: true, creationAttributes: true, creationSkills: true,
                        productionCreationOverview: true, attributesDecorator: service => attributes = new(service),
                        skillsDecorator: service => skills = new(service));
                    var before = PrepareActualFinalizationReadyContext(runtime, stopBeforeAttributes: true,
                        buildMethod: scenario == "sum" ? CharacterCreationBuildMethods.SumToTen : CharacterCreationBuildMethods.Priority,
                        attributeTalent: scenario == "awakened" ? "Mystic Adept" : null);
                    await HydrateFinalizationOwnerAsync(runtime, owners, before);
                    var coordinator = runtime.Coordinator;
                    // Match the real route: startup restoration finishes before
                    // the editor issues the immutable review authority.
                    await coordinator.InitializeAsync();
                    await HydrateFinalizationOwnerAsync(runtime, owners, before);
                    var authority = coordinator.LoadCreationAttributes().Value!;
                    var draft = new CreationAttributesPhoneDraft();
                    draft.Bind(authority, coordinator.State);
                    var allocations = draft.ChangedAllocations(authority,
                        scenario == "awakened" ? "MAG" : "BOD", 1, 0)!.ToArray();
                    var preview = coordinator.PreviewCreationAttributes(authority.Binding, allocations).Value!;
                    Require(coordinator.CanOfferCreationAttributesConfirmation(preview, allocations),
                        "SETUP: UI-shaped allocation review was not admitted.");
                    var editor = new CreationAttributesPage(coordinator, authority);
                    var navigation = new NavigationPage(editor);
                    var review = new CreationAttributesPreviewPage(coordinator, preview, allocations);
                    await navigation.PushAsync(review, animated: false);
                    var window = new Window(navigation);
                    using var alerts = new IssuedPageAlerts(review, window);
                    await alerts.PreflightAsync();
                    await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(review, "OnAppearing")));
                    var save = MinimalVisible(review).OfType<Button>().Single(x => x.AutomationId == "creation-attributes-confirm");
                    Require(save.IsEnabled, "SETUP: actual attribute preview cannot be saved.");
                    skills!.BeforeLoad = () =>
                    {
                        Require(SynchronizationContext.Current != ui, "Next-step load blocked the UI thread.");
                        if (scenario == "load-fault") throw new IOException("synthetic-next-read-failure");
                        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        ui.Post(_ =>
                        {
                            try
                            {
                                ((IButtonController)save).SendClicked();
                                if (scenario is "owner-b" or "owner-aba") owners.Set(ContactsOwnerB);
                                if (scenario == "owner-aba") owners.Set(ContactsOwnerA);
                                if (scenario is "departure" or "return") IssuedPageLifecycle(review, "OnDisappearing");
                                if (scenario == "return") IssuedPageLifecycle(review, "OnAppearing");
                                observed.SetResult();
                            }
                            catch (Exception error) { observed.SetException(error); }
                        }, null);
                        Require(observed.Task.Wait(TimeSpan.FromSeconds(5)), "Next-step load stopped the UI heartbeat.");
                        observed.Task.GetAwaiter().GetResult();
                    };
                    if (scenario == "unknown") attributes!.AfterConfirm = () => throw new IOException("synthetic-confirm-result-loss");
                    await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((IButtonController)save).SendClicked()));
                    var saved = new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!;
                    Require(attributes!.ConfirmCalls == 1 && saved.ContentRevision == before.ContentRevision + 1
                        && saved.SavedRevision == saved.ContentRevision, "Attribute save did not persist exactly once.");
                    bool advanced = scenario is "forward" or "sum" or "awakened";
                    if (advanced)
                    {
                        Require(navigation.Navigation.NavigationStack.Last() is CreationSkillsPage
                            && !navigation.Navigation.NavigationStack.Contains(review),
                            "Saving Attributes stranded the user on its receipt instead of opening Skills.");
                        skills.BeforeLoad = null;
                        var next = (CreationSkillsPage)navigation.Navigation.NavigationStack.Last();
                        await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(next, "OnAppearing")));
                        Require(MinimalVisible(next).OfType<Button>().Any(x => x.AutomationId?.StartsWith("creation-skills-group-", StringComparison.Ordinal) == true
                            && x.AutomationId.EndsWith("-increase", StringComparison.Ordinal)),
                            "Next page did not render actual Core-backed skill controls.");
                        IssuedPageLifecycle(next, "OnDisappearing");
                        await navigation.PopAsync(animated: false);
                        Require(ReferenceEquals(navigation.Navigation.NavigationStack.Last(), editor),
                            "Back returned to a consumed review instead of the Attributes editor.");
                    }
                    else Require(ReferenceEquals(navigation.Navigation.NavigationStack.Last(), review),
                        "A failed, uncertain or departed continuation stole navigation.");
                    if (scenario == "unknown") Require(skills.LoadCalls == 0, "An uncertain commit entered the next step.");
                    if (scenario == "load-fault")
                    {
                        Require(MinimalVisibleText(review).Contains(CreationAllocationStrings.Get("AttributesPreview.NextUnavailable", "missing"), StringComparison.Ordinal),
                            "Failed continuation hid the committed save or its recovery action.");
                        skills.BeforeLoad = null;
                        var retry = MinimalVisible(review).OfType<Button>().Single(x => x.AutomationId == "creation-attributes-continue-skills");
                        await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((IButtonController)retry).SendClicked()));
                        Require(navigation.Navigation.NavigationStack.Last() is CreationSkillsPage && skills.LoadCalls == 2,
                            "Continue did not retry just the next-step read.");
                    }
                    Require(!save.IsEnabled, "A retired save button was re-enabled.");
                    ((IButtonController)save).SendClicked();
                    Require(attributes.ConfirmCalls == 1 && skills.ConfirmCalls == 0 && skills.PreviewCalls == 0 && alerts.Titles.Count == 0,
                        "Forward/retry replayed a mutation or raised an unhandled alert.");
                    RequireSameRewardDocument(saved, new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!);
                    skills.BeforeLoad = null;
                    if (advanced)
                    {
                        await HydrateFinalizationOwnerAsync(runtime, owners, saved);
                        var reopened = coordinator.LoadCreationSkills().Value!;
                        Require(CreationSkillsPhoneAuthority.IsReady(reopened, coordinator.State)
                            && reopened.AttributesDraft is not null, "Cold-store reopen lost the saved Attributes prerequisite.");
                        RequireSameRewardDocument(saved, new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!);
                    }
                    IssuedPageLifecycle(review, "OnDisappearing");
                    Console.WriteLine("PASS Attributes forward: " + scenario);
                }
            }
            finally { CultureInfo.CurrentUICulture = culture; }
        });
    }

    private sealed class SkillsForwardProbe(IOwnerBoundCharacterCreationSkillsService inner) : IOwnerBoundCharacterCreationSkillsService
    {
        public Action? BeforeLoad;
        public int LoadCalls, PreviewCalls, ConfirmCalls;
        public CharacterCreationFoundationResult<CharacterCreationSkillsState> Load(OwnerContextStamp owner, CharacterCreationSkillsLoadRequest request)
        { LoadCalls++; BeforeLoad?.Invoke(); return inner.Load(owner, request); }
        public CharacterCreationFoundationResult<CharacterCreationSkillsPreview> Preview(OwnerContextStamp owner, CharacterCreationSkillsPreviewRequest request)
        { PreviewCalls++; return inner.Preview(owner, request); }
        public CharacterCreationFoundationResult<CharacterCreationSkillsReceipt> Confirm(OwnerContextStamp owner, CharacterCreationSkillsConfirmRequest request)
        { ConfirmCalls++; return inner.Confirm(owner, request); }
    }
}
