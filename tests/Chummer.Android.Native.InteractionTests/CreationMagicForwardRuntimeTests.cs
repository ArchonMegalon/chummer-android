using System.Globalization;
using Chummer.Android.Native;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Workspaces;
using Chummer.Presentation.Overview;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    internal static async Task RunMagicForwardAsync(string contentRoot, string? onlyScenario = null, string? seedDirectory = null)
    {
        Require(seedDirectory is null || onlyScenario == "adept" && Path.IsPathFullyQualified(seedDirectory)
            && Directory.Exists(seedDirectory) && !Directory.EnumerateFileSystemEntries(seedDirectory).Any(),
            "Seed export requires an explicit empty directory and the actual Adept route.");
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            var culture = CultureInfo.CurrentUICulture;
            try
            {
                string[] scenarios = ["magician", "technomancer", "mystic", "adept", "load-fault",
                    "owner-b", "owner-aba", "departure", "return", "stack-change", "unknown", "cold-receipt", "cold-editor",
                    "cold-resolve", "cold-resolve-fault"];
                Require(onlyScenario is null || scenarios.Contains(onlyScenario), "Unknown Magic forward scenario.");
                foreach (string scenario in scenarios.Where(x => onlyScenario is null || x == onlyScenario))
                {
                    CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(
                        scenario == "technomancer" ? "de-AT" : scenario == "load-fault" ? "es-MX" : "en-GB");
                    var owners = new ControlledLinkedOwner();
                    MagicForwardProbe? magic = null;
                    MagicNextResourcesProbe? resources = null;
                    await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners,
                        creationFinalization: true, creationAttributes: true, creationSkills: true,
                        productionCreationOverview: true, magicDecorator: service => magic = new(service),
                        resourcesDecorator: presenter => resources = new(presenter));
                    PrepareActualFinalizationReadyContext(runtime, stopBeforeQualities: true,
                        buildMethod: scenario == "technomancer" ? CharacterCreationBuildMethods.SumToTen : CharacterCreationBuildMethods.Priority,
                        attributeTalent: scenario switch
                        {
                            "technomancer" => CharacterCreationMagicResonanceKinds.Technomancer,
                            "mystic" => "Mystic Adept", // Canonical Priority option spelling, not the projected kind token.
                            "adept" => CharacterCreationMagicResonanceKinds.Adept,
                            _ => CharacterCreationMagicResonanceKinds.Magician
                        }, attributeTalentRank: scenario == "adept" ? "C" : "A");
                    var qualities = runtime.Services.GetRequiredService<ICharacterCreationQualitiesService>();
                    var qualityPreview = qualities.Preview(new(qualities.Load(new(runtime.Id)).Value!.Binding, [])).Value!;
                    Require(qualities.Confirm(new(qualityPreview.Binding, [], qualityPreview.PreviewDigest,
                        "magic-forward-qualities", Guid.NewGuid(), true)).Outcome == CharacterCreationFoundationOutcomes.Success,
                        "SETUP: actual Core rejected Qualities.");
                    var before = new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!;
                    var coordinator = runtime.Coordinator;
                    await HydrateFinalizationOwnerAsync(runtime, owners, before);
                    await coordinator.InitializeAsync();
                    await HydrateFinalizationOwnerAsync(runtime, owners, before);
                    var original = coordinator.State;
                    var load = await coordinator.LoadCreationMagicResonanceForDisplayAsync(original, CancellationToken.None);
                    Require(CreationMagicResonancePhoneAuthority.TryProjectForOverview(load.Value, original, out var projected),
                        "SETUP: Magic projection is not current.");
                    var projection = projected!;
                    CharacterCreationAdeptPowerAllocation[] powers = scenario == "adept"
                        ? new[] { ("Light Body", 4), ("Adrenaline Boost", 4), ("Missile Parry", 4), ("Traceless Walk", 1) }
                            .Select(choice => new CharacterCreationAdeptPowerAllocation(
                                projection.AdeptPowers.Single(x => x.Name == choice.Item1).Identity, choice.Item2)).ToArray()
                        : [];
                    var selections = new CharacterCreationMagicResonanceSelections(
                        load.Value!.TraditionBudget.Total > 0 ? projection.Traditions.First(x => x.IsEnabled).Identity : null,
                        load.Value.StreamBudget.Total > 0 ? projection.Streams.First(x => x.IsEnabled).Identity : null,
                        powers,
                        projection.Spells.Where(x => x.IsEnabled).Take((int)load.Value.SpellBudget.Total).Select(x => x.Identity).ToArray(),
                        projection.ComplexForms.Where(x => x.IsEnabled).Take((int)load.Value.ComplexFormBudget.Total).Select(x => x.Identity).ToArray());
                    var draft = CreationMagicResonancePhoneAuthority.CreateDraft(projection, selections);
                    var checkedReview = await coordinator.ReviewCreationMagicResonanceForDisplayAsync(original, projection, draft);
                    Require(checkedReview.Preview.CanConfirm, "SETUP: actual Core rejected selections: " + string.Join(",", checkedReview.Preview.Blockers));
                    var backend = new MagicForwardBackend();
                    var journal = new CharacterCreationMagicResonanceCheckpointStore(backend);
                    Require(journal.TryCreate(CharacterCreationMagicResonanceCheckpoint.CreateReviewed(checkedReview),
                        out var reviewed, out var blocker), blocker);
                    var editor = new CreationMagicResonancePage(coordinator, journal);
                    var navigation = new NavigationPage(editor);
                    var review = new CreationMagicResonanceReviewPage(coordinator, reviewed, journal);
                    await navigation.PushAsync(review, animated: false);
                    var window = new Window(navigation);
                    using var alerts = new IssuedPageAlerts(review, window);
                    await alerts.PreflightAsync();
                    await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(review, "OnAppearing")));
                    var save = MinimalVisible(review).OfType<Button>().Single(x => x.AutomationId == "creation-magic-resonance-confirm-draft");
                    Require(save.IsEnabled, "SETUP: Magic review disabled.");
                    Exception? observationFailure = null;
                    string openingText = CreationFlowStrings.Get("Magic.Review.OpeningNext", "missing");
                    resources!.BeforeLoad = () =>
                    {
                        Require(SynchronizationContext.Current != ui, "Resources preparation blocked UI.");
                        if (scenario is "load-fault" or "cold-receipt" or "cold-editor") throw new IOException("synthetic-next-read-failure");
                        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        ui.Post(async _ =>
                        {
                            try
                            {
                                Require(!save.IsEnabled && save.Text == openingText,
                                    "Saved Magic lacks visible opening feedback.");
                                ((IButtonController)save).SendClicked();
                                if (scenario is "owner-b" or "owner-aba") owners.Set(ContactsOwnerB);
                                if (scenario == "owner-aba") owners.Set(ContactsOwnerA);
                                if (scenario is "departure" or "return") IssuedPageLifecycle(review, "OnDisappearing");
                                if (scenario == "return") IssuedPageLifecycle(review, "OnAppearing");
                                if (scenario == "stack-change") await navigation.PushAsync(new ContentPage(), animated: false);
                                observed.SetResult();
                            }
                            catch (Exception error) { observationFailure = error; observed.SetException(error); }
                        }, null);
                        Require(observed.Task.Wait(TimeSpan.FromSeconds(5)), "Resources read prevented UI feedback.");
                        observed.Task.GetAwaiter().GetResult();
                    };
                    bool uncertain = scenario is "unknown" or "cold-resolve" or "cold-resolve-fault";
                    if (uncertain) magic!.AfterConfirm = () => throw new IOException("synthetic-result-loss");
                    await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((IButtonController)save).SendClicked()));
                    if (observationFailure is not null) throw observationFailure;
                    resources.BeforeLoad = null;
                    var saved = new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!;
                    Require(magic!.ConfirmCalls == 1 && saved.ContentRevision == before.ContentRevision + 1
                        && saved.SavedRevision == saved.ContentRevision && saved.Document.Content == before.Document.Content,
                        "Magic did not save exactly one auxiliary draft.");
                    bool advanced = scenario is "magician" or "technomancer" or "mystic" or "adept";
                    if (advanced)
                    {
                        Require(navigation.Navigation.NavigationStack.Last() is CreationResourcesPage
                            && !navigation.Navigation.NavigationStack.Contains(review),
                            "Magic confirmation did not advance directly to Resources: " + scenario);
                        Require(!journal.TryRead(out _, out _), "Successful forward retained a Confirmed claim.");
                        var next = (NativePageBase)navigation.Navigation.NavigationStack.Last();
                        int loads = resources.LoadCalls;
                        await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(next, "OnAppearing")));
                        Require(resources.LoadCalls == loads && MinimalVisible(next).OfType<Button>().Any(x => x.IsEnabled),
                            "Resources discarded its one-use load or has no usable action.");
                        IssuedPageLifecycle(next, "OnDisappearing");
                        await navigation.PopAsync(animated: false);
                        Require(ReferenceEquals(navigation.Navigation.NavigationStack.Last(), editor), "Back returned to consumed Magic review.");
                    }
                    else
                    {
                        Require(scenario == "stack-change" || ReferenceEquals(navigation.Navigation.NavigationStack.Last(), review),
                            "Failed/stale/uncertain continuation stole navigation.");
                        Require(navigation.Navigation.NavigationStack.Last() is not CreationResourcesPage
                            && journal.TryRead(out var retained, out _)
                            && retained.Phase == (uncertain ? CharacterCreationMagicResonanceCheckpointPhase.Confirming
                                : CharacterCreationMagicResonanceCheckpointPhase.Confirmed), "Recovery claim was discarded.");
                    }
                    if (scenario is "load-fault" or "cold-receipt" or "cold-editor")
                    {
                        Require(MinimalVisibleText(review).Contains(CreationFlowStrings.Get("Magic.Review.NextUnavailable", "missing")),
                            "Resources failure hid successful save.");
                        if (scenario is "cold-receipt" or "cold-editor")
                        {
                            IssuedPageLifecycle(review, "OnDisappearing");
                            await HydrateFinalizationOwnerAsync(runtime, owners, saved);
                            var coldJournal = new CharacterCreationMagicResonanceCheckpointStore(backend);
                            Require(coldJournal.TryRead(out var confirmed, out _), "Cold journal lost saved claim.");
                            NativePageBase receipt = scenario == "cold-receipt"
                                ? new CreationMagicResonanceReceiptPage(coordinator, confirmed, confirmed.Confirmation!, coldJournal)
                                : new CreationMagicResonancePage(coordinator, coldJournal);
                            await navigation.PushAsync(receipt, animated: false);
                            await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(receipt, "OnAppearing")));
                            string resumeId = scenario == "cold-receipt" ? "creation-magic-resonance-receipt-acknowledge"
                                : "creation-magic-resonance-open-receipt";
                            var resume = MinimalVisible(receipt).OfType<Button>().Single(x => x.AutomationId == resumeId);
                            await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((IButtonController)resume).SendClicked()));
                        }
                        else
                        {
                            var retry = MinimalVisible(review).OfType<Button>().Single(x => x.AutomationId == "creation-magic-resonance-continue-next");
                            Require(retry.IsEnabled, "Saved review has no read-only Continue.");
                            await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((IButtonController)retry).SendClicked()));
                        }
                        Require(navigation.Navigation.NavigationStack.Last() is CreationResourcesPage && !journal.TryRead(out _, out _),
                            "Read-only retry/recovery did not advance.");
                    }
                    if (scenario == "unknown") Require(resources.LoadCalls == 0, "Unknown result advanced without confirmed receipt.");
                    if (scenario is "cold-resolve" or "cold-resolve-fault")
                    {
                        Require(resources.LoadCalls == 0, "Uncertain save started continuation before reconciliation.");
                        magic.AfterConfirm = null;
                        IssuedPageLifecycle(review, "OnDisappearing");
                        await HydrateFinalizationOwnerAsync(runtime, owners, saved);
                        var coldJournal = new CharacterCreationMagicResonanceCheckpointStore(backend);
                        var resumed = new CreationMagicResonancePage(coordinator, coldJournal);
                        await navigation.PushAsync(resumed, animated: false);
                        await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(resumed, "OnAppearing")));
                        var resolve = MinimalVisible(resumed).OfType<Button>().Single(x => x.AutomationId == "creation-magic-resonance-resolve-confirming");
                        if (scenario == "cold-resolve-fault") resources.BeforeLoad = () => throw new IOException("synthetic-recovery-read-failure");
                        await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((IButtonController)resolve).SendClicked()));
                        resources.BeforeLoad = null;
                        if (scenario == "cold-resolve-fault")
                        {
                            Require(navigation.Navigation.NavigationStack.Last() is CreationMagicResonanceReceiptPage,
                                "A reconciled save with failed next read left a stale editor instead of saved-state recovery.");
                            var receipt = (CreationMagicResonanceReceiptPage)navigation.Navigation.NavigationStack.Last();
                            await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(receipt, "OnAppearing")));
                            var retry = MinimalVisible(receipt).OfType<Button>().Single(x => x.AutomationId == "creation-magic-resonance-receipt-acknowledge");
                            await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((IButtonController)retry).SendClicked()));
                        }
                        Require(navigation.Navigation.NavigationStack.Last() is CreationResourcesPage
                            && !coldJournal.TryRead(out _, out _), "Exact interrupted-command reconciliation did not continue.");
                        IssuedPageLifecycle(resumed, "OnDisappearing");
                    }
                    // A retained, disabled control is rejected synchronously;
                    // there is deliberately no asynchronous operation to join.
                    ((IButtonController)save).SendClicked();
                    Require(magic.ConfirmCalls == (scenario is "cold-resolve" or "cold-resolve-fault" ? 2 : 1)
                        && resources.ConfirmCalls == 0 && alerts.Titles.Count == 0,
                        "Continuation replayed mutation or exposed raw error.");
                    RequireSameRewardDocument(saved, new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!);
                    if (seedDirectory is not null)
                    {
                        // Export only this synthetic local runner, never the linked
                        // account clone or phone preferences. Core and the actual
                        // native confirm handler produced every saved byte.
                        string workspaces = Path.Combine(runtime.StateDirectory, "workspaces");
                        string target = Path.Combine(seedDirectory, "workspaces");
                        Directory.CreateDirectory(target);
                        foreach (string suffix in new[] { ".json", ".json.slot", ".json.lock" })
                            File.Copy(Path.Combine(workspaces, runtime.Id.Value + suffix),
                                Path.Combine(target, runtime.Id.Value + suffix), overwrite: false);
                        Console.WriteLine($"SEED Magic {runtime.Id.Value} revision={saved.ContentRevision}/{saved.SavedRevision}");
                    }
                    IssuedPageLifecycle(review, "OnDisappearing");
                    Console.WriteLine("PASS Magic forward: " + scenario);
                }
            }
            finally { CultureInfo.CurrentUICulture = culture; }
        });
    }

    private sealed class MagicForwardBackend : ICharacterCreationMagicResonanceCheckpointBackend
    {
        private string _payload = string.Empty;
        public string Read() => _payload;
        public void Write(string payload) => _payload = payload;
        public void Remove() => _payload = string.Empty;
    }

    private sealed class MagicForwardProbe(IOwnerBoundCharacterCreationMagicResonanceService inner)
        : IOwnerBoundCharacterCreationMagicResonanceService
    {
        public Action? AfterConfirm;
        public int ConfirmCalls;
        public CharacterCreationFoundationResult<CharacterCreationMagicResonanceState> Load(OwnerContextStamp owner, CharacterCreationMagicResonanceLoadRequest request)
            => inner.Load(owner, request);
        public CharacterCreationFoundationResult<CharacterCreationMagicResonancePreview> Preview(OwnerContextStamp owner, CharacterCreationMagicResonancePreviewRequest request)
            => inner.Preview(owner, request);
        public CharacterCreationFoundationResult<CharacterCreationMagicResonanceReceipt> Confirm(OwnerContextStamp owner, CharacterCreationMagicResonanceConfirmRequest request)
        { ConfirmCalls++; var result = inner.Confirm(owner, request); AfterConfirm?.Invoke(); return result; }
    }

    private sealed class MagicNextResourcesProbe(ICharacterCreationResourcesInteractionPresenter inner)
        : ICharacterCreationResourcesInteractionPresenter
    {
        public Action? BeforeLoad;
        public int LoadCalls, ConfirmCalls;
        public CharacterCreationResourcesInteractionLoadResult Load(CharacterOverviewState display)
        { LoadCalls++; BeforeLoad?.Invoke(); return inner.Load(display); }
        public CharacterCreationResourcesInteractionPrepareResult Prepare(CharacterOverviewState display, string optionId) => inner.Prepare(display, optionId);
        public CharacterCreationResourcesInteractionConfirmResult Confirm(CharacterOverviewState display, CharacterCreationResourcesConfirmation confirmation)
        { ConfirmCalls++; return inner.Confirm(display, confirmation); }
        public CharacterCreationResourcesInteractionReceiptLookupResult LookupReceipt(CharacterOverviewState display, string idempotencyKey) => inner.LookupReceipt(display, idempotencyKey);
    }
}
