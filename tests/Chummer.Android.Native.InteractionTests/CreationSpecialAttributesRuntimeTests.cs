using System.Globalization;
using Chummer.Android.Native;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Workspaces;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    internal static async Task RunSpecialAttributesAsync(string contentRoot, string? smokePath)
    {
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            foreach (var (talent, rank, attributeId) in new[]
            {
                ("Mystic Adept", "C", "MAG"), ("Mystic Adept", "A", "MAG"),
                ("Adept", "C", "MAG"), ("Technomancer", "C", "RES")
            })
            {
                var owners = new ControlledLinkedOwner();
                AttributesCommitProbe? probe = null;
                await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners,
                    creationFinalization: true, creationAttributes: true, productionCreationOverview: true,
                    attributesDecorator: actual => probe = new(actual));
                var before = PrepareActualFinalizationReadyContext(runtime, stopBeforeAttributes: true,
                    fixtureAlias: "SpecialMagic" + rank, attributeTalent: talent, attributeTalentRank: rank);
                await HydrateFinalizationOwnerAsync(runtime, owners, before);
                var state = runtime.Coordinator.LoadCreationAttributes().Value!;
                if (talent == "Mystic Adept" && rank == "C")
                {
                    await VerifySpecialPointPendingFeedbackAsync(ui, runtime, owners, probe!, state);
                    RequireSameRewardDocument(before, new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!);
                    await HydrateFinalizationOwnerAsync(runtime, owners, before);
                    state = runtime.Coordinator.LoadCreationAttributes().Value!;
                }
                var draft = new CreationAttributesPhoneDraft();
                draft.Bind(state, runtime.Coordinator.State);
                var editor = new CreationAttributeAllocationPage(runtime.Coordinator, draft, attributeId, state);
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(editor, "OnAppearing")));
                var attribute = draft.Attribute(state, attributeId)!;
                var plus = MinimalVisible(editor).OfType<Button>().Single(x =>
                    x.AutomationId == "creation-attribute-priority-increase-" + attributeId.ToLowerInvariant());
                Require(draft.SpecialBudget(state).Remaining == 7,
                    "Magic/Resonance fixture must retain the expected seven special points before testing the cap.");
                Console.WriteLine($"CHECK {talent}/{rank}: {attributeId} {attribute.Current}/{attribute.Maximum}, special {draft.SpecialBudget(state).Remaining}, enabled {plus.IsEnabled}");
                Require(plus.IsEnabled == (attribute.Current < attribute.Maximum),
                    "Magic/Resonance special + does not match the actual Core cap with available points.");
                if (plus.IsEnabled)
                {
                    await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((IButtonController)plus).SendClicked()));
                    Require(draft.Attribute(state, attributeId)!.Current == attribute.Current + 1
                        && draft.SpecialBudget(state).Remaining == state.SpecialPointBudget.Remaining - 1,
                        "Magic/Resonance + did not allocate one special point.");
                    var minus = MinimalVisible(editor).OfType<Button>().Single(x =>
                        x.AutomationId == "creation-attribute-priority-decrease-" + attributeId.ToLowerInvariant());
                    await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((IButtonController)minus).SendClicked()));
                    Require(draft.Attribute(state, attributeId)!.Current == attribute.Current
                        && draft.SpecialBudget(state).Remaining == state.SpecialPointBudget.Remaining,
                        "Magic/Resonance - did not return the special point.");
                    RequireSameRewardDocument(before, new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!);
                    plus = MinimalVisible(editor).OfType<Button>().Single(x =>
                        x.AutomationId == "creation-attribute-priority-increase-" + attributeId.ToLowerInvariant());
                    await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((IButtonController)plus).SendClicked()));
                    var allocations = draft.Allocations(state);
                    var preview = runtime.Coordinator.PreviewCreationAttributes(state.Binding, allocations).Value!;
                    CreationAttributesPhoneConfirmResult? applied = null;
                    var review = new CreationAttributesPreviewPage(runtime.Coordinator, preview, allocations, result => applied = result);
                    MinimalRender(review);
                    var confirm = MinimalVisible(review).OfType<Button>().Single(x => x.AutomationId == "creation-attributes-confirm");
                    Require(confirm.IsEnabled, "Legal Magic/Resonance special point cannot be confirmed.");
                    await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((IButtonController)confirm).SendClicked()));
                    Require(applied is { Outcome: CharacterCreationFoundationOutcomes.Success, Receipt: not null },
                        "Magic/Resonance special point confirmation failed.");
                    var saved = new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!;
                    Require(saved.ContentRevision == before.ContentRevision + 1 && saved.SavedRevision == saved.ContentRevision,
                        "Magic/Resonance special point must save exactly one revision.");
                    await HydrateFinalizationOwnerAsync(runtime, owners, saved);
                    var reopened = runtime.Coordinator.LoadCreationAttributes().Value!;
                    Require(reopened.Attributes.Single(x => x.AttributeId == attributeId).Current == attribute.Current + 1
                        && reopened.SpecialPointBudget.Remaining == state.SpecialPointBudget.Remaining - 1
                        && reopened.NormalPointBudget == state.NormalPointBudget
                        && reopened.CreationKarmaBudget == state.CreationKarmaBudget,
                        "Magic/Resonance allocation or independent budgets changed on cold-store reopen.");
                    RequireSameRewardDocument(saved, new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!);
                    MinimalRender(review);
                    Require(!MinimalVisible(review).OfType<Button>().Any(x => x.AutomationId == "creation-attributes-confirm"),
                        "Saved Magic/Resonance review permits duplicate confirmation.");
                    if (talent == "Mystic Adept" && rank == "C" && smokePath is not null)
                    {
                        Require(Path.IsPathFullyQualified(smokePath), "Use an explicit private smoke seed path.");
                        File.Copy(Path.Combine(runtime.StateDirectory, "workspaces", runtime.Id.Value + ".json"),
                            Path.ChangeExtension(smokePath, ".magic.json"), overwrite: false);
                    }
                    Console.WriteLine($"PASS {talent}/{rank} {attributeId} allocation, one save and cold-store reopen with unchanged normal/Karma pools");
                }
                else
                {
                    var culture = CultureInfo.CurrentUICulture;
                    try
                    {
                        foreach (string locale in new[] { "en-GB", "de-AT", "es-MX" })
                        {
                            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(locale);
                            MinimalRender(editor);
                            string expected = CreationAllocationStrings.Format("Attributes.MaximumReached", "missing",
                                CreationAttributesPage.AttributeLabel(attributeId), attribute.Maximum);
                            var reason = MinimalVisible(editor).OfType<Label>().Single(x =>
                                x.AutomationId == plus.AutomationId + "-reason");
                            Require(reason.Text.Contains(expected, StringComparison.Ordinal),
                                "Capped Magic must explain the exact Core maximum in " + locale);
                            MinimalRequireNoMachineValues(editor);
                            Require(!MinimalVisible(editor).OfType<Button>().Single(x => x.AutomationId == plus.AutomationId).IsEnabled,
                                "Readable limit guidance enabled an illegal Magic allocation.");
                        }
                    }
                    finally { CultureInfo.CurrentUICulture = culture; }
                    RequireSameRewardDocument(before, new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!);
                }
                IssuedPageLifecycle(editor, "OnDisappearing");
            }
            foreach (string method in new[] { CharacterCreationBuildMethods.Priority, CharacterCreationBuildMethods.SumToTen })
            {
                var owners = new ControlledLinkedOwner();
                await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners,
                    creationFinalization: true, creationAttributes: true, productionCreationOverview: true);
                var before = PrepareActualFinalizationReadyContext(runtime, buildMethod: method, stopBeforeAttributes: true,
                    fixtureAlias: "SpecialPoints" + method);
                await HydrateFinalizationOwnerAsync(runtime, owners, before);
                var coordinator = runtime.Coordinator;
                var state = coordinator.LoadCreationAttributes().Value!;
                Require(state.SpecialPointBudget.Remaining > 0 && state.Attributes.Single(x => x.AttributeId == "EDG").IsEnabled,
                    "SETUP: actual Priority Human must have spendable special points.");
                var page = new CreationAttributesPage(coordinator, state);
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing")));
                var scroll = (ScrollView)page.Content!;
                Element? target = null;
                ((IScrollViewController)scroll).ScrollToRequested += (_, request) =>
                {
                    target = request.Element;
                    ((IScrollViewController)scroll).SendScrollFinished();
                };
                var specialJump = MinimalVisible(page).OfType<Button>().SingleOrDefault(x =>
                    x.AutomationId == "creation-attributes-budget-special-jump");
                Require(specialJump is { IsEnabled: true }, "Special points have no clickable allocation entry.");
                ((IButtonController)specialJump!).SendClicked();
                Require(target?.AutomationId == "creation-attributes-special-heading", "Special points jumped to the wrong group.");
                var normalJump = MinimalVisible(page).OfType<Button>().Single(x =>
                    x.AutomationId == "creation-attributes-budget-normal-jump");
                ((IButtonController)normalJump).SendClicked();
                Require(target?.AutomationId == "creation-attributes-normal-heading", "Normal points lost their own destination.");
                target = null;
                MinimalRender(page);
                ((IButtonController)specialJump!).SendClicked();
                Require(target is null, "Detached special points entry still navigates.");
                var draft = new CreationAttributesPhoneDraft();
                draft.Bind(state, coordinator.State);
                var editor = new CreationAttributeAllocationPage(coordinator, draft, "EDG", state);
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(editor, "OnAppearing")));
                Button Plus() => MinimalVisible(editor).OfType<Button>().Single(x =>
                    x.AutomationId == "creation-attribute-priority-increase-edg");
                Button Minus() => MinimalVisible(editor).OfType<Button>().Single(x =>
                    x.AutomationId == "creation-attribute-priority-decrease-edg");
                var culture = CultureInfo.CurrentUICulture;
                try
                {
                    foreach (string locale in new[] { "en-GB", "de-AT", "es-MX" })
                    {
                        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(locale);
                        MinimalRender(editor);
                        Require(Plus().IsEnabled && Plus().Text == CreationAllocationStrings.Get("AttributeAllocation.SpecialIncrease", "missing")
                            && Minus().Text == CreationAllocationStrings.Get("AttributeAllocation.SpecialDecrease", "missing"),
                            "Special point controls are disabled or wrongly labelled in " + locale);
                        MinimalRequireNoMachineValues(editor);
                        Require(!MinimalVisibleText(editor).Contains("\nEDG\n", StringComparison.Ordinal),
                            "Attribute editor leaks its typed ID in " + locale);
                        MinimalRender(page);
                        Require(!MinimalVisibleText(page).Contains("creation-attributes-", StringComparison.Ordinal)
                            && MinimalVisibleText(page).Contains(CreationAllocationStrings.Get("Attributes.EssenceNotSpendable", "missing")),
                            "Disabled special attributes must explain availability without raw codes in " + locale);
                    }
                }
                finally { CultureInfo.CurrentUICulture = culture; MinimalRender(editor); }
                int initialEdge = draft.Attribute(state, "EDG")!.Current;
                decimal remaining = draft.SpecialBudget(state).Remaining;
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((IButtonController)Plus()).SendClicked()));
                Require(draft.Attribute(state, "EDG")!.Current == initialEdge + 1
                    && draft.SpecialBudget(state).Remaining == remaining - 1
                    && draft.NormalBudget(state) == state.NormalPointBudget
                    && draft.KarmaBudget(state) == state.CreationKarmaBudget,
                    "Edge + did not spend exactly one special point independently of normal points and Karma.");
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((IButtonController)Minus()).SendClicked()));
                Require(draft.Attribute(state, "EDG")!.Current == initialEdge
                    && draft.SpecialBudget(state).Remaining == remaining, "Edge - did not return the special point.");
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((IButtonController)Plus()).SendClicked()));
                var allocations = draft.Allocations(state);
                RequireSameRewardDocument(before, new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!);
                var preview = coordinator.PreviewCreationAttributes(state.Binding, allocations).Value!;
                CreationAttributesPhoneConfirmResult? applied = null;
                var review = new CreationAttributesPreviewPage(coordinator, preview, allocations, result => applied = result);
                foreach (string locale in new[] { "en-GB", "de-AT", "es-MX" })
                {
                    CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(locale);
                    VerifyQualityDisclosure(review, "creation-attributes-preview-details", "creation-attributes-confirm",
                        preview.PreviewDigest, preview.Binding.RawCharacterXmlDigest, preview.Binding.AuxiliaryStateDigest);
                    var value = MinimalVisible(review).OfType<Label>().Single(x => x.AutomationId == "creation-attributes-preview-value-edg");
                    Require(value.Text == (initialEdge + 1).ToString(CultureInfo.InvariantCulture)
                        && value.FontSize >= 28 && value.FontAttributes.HasFlag(FontAttributes.Bold)
                        && !MinimalVisibleText(review).Contains("\nEDG\n", StringComparison.Ordinal)
                        && MinimalVisibleText(review).Contains(CreationAllocationStrings.Get("AttributeAllocation.SpecialSpent", "missing")),
                        "Review lost readable special allocation value/cost in " + locale);
                }
                CultureInfo.CurrentUICulture = culture;
                MinimalRender(review);
                RequireSameRewardDocument(before, new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!);
                var confirm = MinimalVisible(review).OfType<Button>().Single(x => x.AutomationId == "creation-attributes-confirm");
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((IButtonController)confirm).SendClicked()));
                Require(applied is { Outcome: CharacterCreationFoundationOutcomes.Success, Receipt: not null },
                    "Special point allocation could not be saved.");
                var saved = new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!;
                Require(saved.ContentRevision == before.ContentRevision + 1 && saved.SavedRevision == saved.ContentRevision,
                    "Special point confirmation must save exactly one revision.");
                foreach (string locale in new[] { "en-GB", "de-AT", "es-MX" })
                {
                    CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(locale);
                    VerifyQualityDisclosure(review, "creation-attributes-preview-details", "creation-attributes-back-to-build",
                        applied!.Receipt!.DraftDigest, applied.RefreshedState!.Binding.AuxiliaryStateDigest);
                    Require(MinimalVisibleText(review).Contains(CreationAllocationStrings.Get("AttributesPreview.SavedHeading", "missing"), StringComparison.OrdinalIgnoreCase)
                        && !MinimalVisibleText(review).Contains("\nEDG\n", StringComparison.Ordinal),
                        "Saved receipt must show localized result, not typed IDs.");
                }
                CultureInfo.CurrentUICulture = culture;
                RequireSameRewardDocument(saved, new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!);
                await HydrateFinalizationOwnerAsync(runtime, owners, saved);
                var reopened = coordinator.LoadCreationAttributes().Value!;
                Require(reopened.Attributes.Single(x => x.AttributeId == "EDG").Current == initialEdge + 1
                    && reopened.SpecialPointBudget.Remaining == remaining - 1, "Special allocation lost on cold-store reopen.");
                // A fully spent pool or an attribute cap must still disable another +.
                var cappedDraft = new CreationAttributesPhoneDraft();
                cappedDraft.Bind(reopened, coordinator.State);
                var edge = cappedDraft.Attribute(reopened, "EDG")!;
                int legalExtra = Math.Min(edge.Maximum - edge.Current, (int)reopened.SpecialPointBudget.Remaining);
                if (legalExtra > 0)
                {
                    var capped = cappedDraft.ChangedAllocations(reopened, "EDG", legalExtra, 0)!;
                    Require(cappedDraft.TryAdopt(reopened, coordinator.State,
                        coordinator.PreviewCreationAttributes(reopened.Binding, capped), capped), "SETUP: Core cap preview rejected.");
                }
                var cappedPage = new CreationAttributeAllocationPage(coordinator, cappedDraft, "EDG", reopened);
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(cappedPage, "OnAppearing")));
                Require(!MinimalVisible(cappedPage).OfType<Button>().Single(x =>
                    x.AutomationId == "creation-attribute-priority-increase-edg").IsEnabled,
                    "Special points bypassed the Core budget or attribute cap.");
                RequireSameRewardDocument(saved, new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!);
                var originalOwner = owners.Current;
                owners.Set(ContactsOwnerB);
                owners.Set(originalOwner);
                MinimalRender(cappedPage);
                Require(!MinimalVisible(cappedPage).OfType<Button>().Any(x => x.IsEnabled), "Owner ABA revived special allocation.");
                MinimalRender(review);
                Require(!MinimalVisible(review).OfType<Button>().Any(), "Owner ABA exposed old review actions or diagnostics.");
                MinimalRequireNoMachineValues(review);
                IssuedPageLifecycle(cappedPage, "OnDisappearing");
                IssuedPageLifecycle(editor, "OnDisappearing");
                IssuedPageLifecycle(page, "OnDisappearing");
                if (method == CharacterCreationBuildMethods.Priority && smokePath is not null)
                {
                    Require(Path.IsPathFullyQualified(smokePath), "Use an explicit private smoke seed path.");
                    File.Copy(Path.Combine(runtime.StateDirectory, "workspaces", runtime.Id.Value + ".json"), smokePath, overwrite: false);
                }
                Console.WriteLine("PASS " + method + " special entry, EN/DE/ES controls, Edge +/-, separate budgets, save/cold reopen, cap and owner ABA");
            }
        });
    }

    private static async Task VerifySpecialPointPendingFeedbackAsync(IssuedPageUiContext ui,
        NativeRewardRuntime runtime, ControlledLinkedOwner owners, AttributesCommitProbe probe,
        CharacterCreationAttributesState state)
    {
        foreach (string outcome in new[] { "ready", "cancel", "error", "leave", "owner-aba" })
        {
            var draft = new CreationAttributesPhoneDraft();
            draft.Bind(state, runtime.Coordinator.State);
            var page = new CreationAttributeAllocationPage(runtime.Coordinator, draft, "MAG", state);
            var navigation = new NavigationPage(new ContentPage());
            await navigation.PushAsync(page, false);
            var window = new Window(navigation);
            using var alerts = new IssuedPageAlerts(page, window);
            await alerts.PreflightAsync();
            await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing")));
            var body = (VerticalStackLayout)((ScrollView)page.Content!).Content!;
            var rendered = body.Children.ToArray();
            var plus = MinimalVisible(page).OfType<Button>().Single(x =>
                x.AutomationId == "creation-attribute-priority-increase-mag");
            string label = plus.Text;
            int initial = draft.Attribute(state, "MAG")!.Current;
            using var release = new ManualResetEventSlim();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int reads = 0;
            probe.BeforeRead = () =>
            {
                Interlocked.Increment(ref reads);
                entered.TrySetResult();
                Require(release.Wait(TimeSpan.FromSeconds(10)), "Pending special point test was not released.");
                if (outcome == "cancel") throw new OperationCanceledException();
                if (outcome is "error" or "owner-aba") throw new InvalidOperationException("special-point-feedback-test");
            };
            Task pending = ui.BeginAsyncVoid(() => ((IButtonController)plus).SendClicked());
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Require(!pending.IsCompleted && !plus.IsEnabled
                    && plus.Text == CreationAllocationStrings.Get("AttributeAllocation.Checking", "Checking points…")
                    && rendered.SequenceEqual(body.Children)
                    && !body.Children.OfType<Button>().Any(x => x.IsEnabled)
                    && !MinimalVisible(page).OfType<ActivityIndicator>().Any(x => x.IsRunning),
                    "Special + must show static feedback on the retained button while every adjustment is disabled.");
                var heartbeat = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                ui.Post(_ => heartbeat.SetResult(), null);
                await heartbeat.Task.WaitAsync(TimeSpan.FromSeconds(2));
                // The existing async-void join owns the pending action. A duplicate
                // click must return synchronously through the page action gate.
                ((IButtonController)plus).SendClicked();
                MinimalRender(page);
                Require(reads == 1 && !pending.IsCompleted && rendered.SequenceEqual(body.Children)
                    && plus.Text != label && draft.Attribute(state, "MAG")!.Current == initial + 1
                    && draft.SpecialBudget(state).Remaining == state.SpecialPointBudget.Remaining - 1,
                    "A duplicate + or pending refresh repeated an adjustment or cleared visible feedback.");
                if (outcome == "leave") IssuedPageLifecycle(page, "OnDisappearing");
            }
            finally
            {
                release.Set();
                try { await JoinIssuedPageAsync(pending); }
                finally { probe.BeforeRead = null; }
            }
            Require(plus.Text == label && !plus.IsEnabled
                && !MinimalVisible(page).OfType<Label>().Any(x =>
                    x.AutomationId == "creation-attribute-allocation-loading" && x.IsVisible),
                "Completion must clear feedback without re-enabling an old button: " + outcome);
            if (outcome == "ready")
            {
                var fresh = MinimalVisible(page).OfType<Button>().Single(x => x.AutomationId == plus.AutomationId);
                Require(!ReferenceEquals(fresh, plus) && fresh.IsEnabled && fresh.Text == label,
                    "Fresh Core preview did not restore the ordinary special + control.");
            }
            else
                Require(!body.Children.OfType<Button>().Any(x => x.IsEnabled
                    && x.AutomationId != "creation-attribute-allocation-retry"),
                    "Canceled, failed or departed preparation enabled stale adjustments: " + outcome);
            Require(alerts.Titles.Count == 0,
                "Point checks must use readable in-page recovery, not raw exception dialogs: " + outcome);
            if (outcome is "cancel" or "error" or "owner-aba")
            {
                Button? Retry() => MinimalVisible(page).OfType<Button>().SingleOrDefault(x =>
                    x.AutomationId == "creation-attribute-allocation-retry");
                Require(Retry() is { IsEnabled: true },
                    "A failed point check stranded the editor without a read-only recovery action.");
                var culture = CultureInfo.CurrentUICulture;
                try
                {
                    foreach (string locale in new[] { "en-GB", "de-AT", "es-MX" })
                    {
                        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(locale);
                        MinimalRender(page);
                        Require(Retry()!.Text == CreationAllocationStrings.Get("AttributeAllocation.CheckAgain", "missing")
                            && MinimalVisibleText(page).Contains(CreationAllocationStrings.Get("AttributeAllocation.CheckFailed", "missing"))
                            && !MinimalVisibleText(page).Contains("special-point-feedback-test")
                            && !MinimalVisibleText(page).Contains("InvalidOperationException"),
                            "Recovery must show localized guidance without raw exception text: " + locale);
                        MinimalRequireNoMachineValues(page);
                    }
                }
                finally { CultureInfo.CurrentUICulture = culture; MinimalRender(page); }
                var allocations = draft.Allocations(state).ToArray();
                Button retained = Retry()!;
                if (outcome == "owner-aba")
                {
                    var owner = owners.Current;
                    owners.Set(ContactsOwnerB);
                    owners.Set(owner);
                    int beforeRetry = reads;
                    probe.BeforeRead = () => Interlocked.Increment(ref reads);
                    try { await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((IButtonController)retained).SendClicked())); }
                    finally { probe.BeforeRead = null; }
                    Require(reads == beforeRetry && Retry() is null && draft.Allocations(state).SequenceEqual(allocations)
                        && !body.Children.OfType<Button>().Any(x => x.IsEnabled),
                        "An owner A→B→A transition admitted retained recovery or changed the draft.");
                    IssuedPageLifecycle(page, "OnDisappearing");
                    continue;
                }
                MinimalRender(page);
                int priorReads = reads;
                probe.BeforeRead = () => Interlocked.Increment(ref reads);
                try { await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((IButtonController)retained).SendClicked())); }
                finally { probe.BeforeRead = null; }
                Require(reads == priorReads, "A detached recovery button entered Core.");
                Button retry = Retry()!;
                release.Reset();
                var retryEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                probe.BeforeRead = () =>
                {
                    Interlocked.Increment(ref reads);
                    retryEntered.TrySetResult();
                    Require(release.Wait(TimeSpan.FromSeconds(10)), "Recovery read was not released.");
                };
                Task retrying = ui.BeginAsyncVoid(() => ((IButtonController)retry).SendClicked());
                try
                {
                    await retryEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    Require(!retry.IsEnabled && retry.Text == CreationAllocationStrings.Get("AttributeAllocation.Checking", "missing"),
                        "Recovery must expose pending feedback before waiting for Core.");
                    ((IButtonController)retry).SendClicked();
                    Require(reads == priorReads + 1 && draft.Allocations(state).SequenceEqual(allocations),
                        "A duplicate recovery replayed the point adjustment or started another read.");
                }
                finally
                {
                    release.Set();
                    try { await JoinIssuedPageAsync(retrying); }
                    finally { probe.BeforeRead = null; }
                }
                Require(Retry() is null && draft.Allocations(state).SequenceEqual(allocations)
                    && draft.Attribute(state, "MAG")!.Current == initial + 1
                    && draft.SpecialBudget(state).Remaining == state.SpecialPointBudget.Remaining - 1
                    && MinimalVisible(page).OfType<Button>().Any(x => x.AutomationId == plus.AutomationId && x.IsEnabled),
                    "Read-only recovery lost or replayed the admitted point instead of restoring fresh controls.");
            }
            else
                Require(!MinimalVisible(page).OfType<Button>().Any(x => x.AutomationId == "creation-attribute-allocation-retry"),
                    "Successful or departed preparation exposed a recovery action.");
            if (outcome != "leave") IssuedPageLifecycle(page, "OnDisappearing");
            await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing")));
            Require(MinimalVisible(page).OfType<Button>().Any(x => x.AutomationId == plus.AutomationId
                && x.IsEnabled && x.Text == label && !ReferenceEquals(x, plus)),
                "Returning to the editor did not replace stale controls after " + outcome);
            Require(probe.ConfirmCalls == 0 && draft.NormalBudget(state) == state.NormalPointBudget
                && draft.KarmaBudget(state) == state.CreationKarmaBudget,
                "Pending feedback saved the runner or changed another budget.");
            IssuedPageLifecycle(page, "OnDisappearing");
        }
        Console.WriteLine("PASS special + pending feedback, read-only failure recovery, localized guidance, duplicate/detached/owner-ABA rejection, UI heartbeat and fresh reappearance");
    }
}
