using System.Globalization;
using System.Reflection;
using System.Xml.Linq;
using Chummer.Android.Native;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Chummer.Presentation.Creation;
using Chummer.Presentation.Overview;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    private static async Task<WorkspaceStoredDocument> PurchaseManualLoadoutThroughGearAsync(
        IssuedPageUiContext ui, NativeRewardRuntime runtime, ControlledLinkedOwner owners, BuildPage root,
        WorkspaceStoredDocument before, string scenario, ManualLoadoutServiceProbe probe)
    {
        var navigation = root.Navigation;
        await WaitForManualGearRouteAsync(ui, root);
        await ClickCompletionControlAsync(ui, CompletionPageControl<Button>(root, "creation-stage-gear"));
        Require(navigation.NavigationStack.Last() is CreationGearPage, "The real Resources → Gear route did not open.");
        var page = (CreationGearPage)navigation.NavigationStack.Last();
        IssuedPageLifecycle(root, "OnDisappearing");
        await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing")));
        var formatter = typeof(CreationGearPage).GetMethod("Nuyen", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var copy = (AndroidSurfaceCopy)typeof(CreationGearPage).GetField("_copy", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
        string displayedFraction = (string)formatter.Invoke(page, [0.125m])!;
        Require(displayedFraction.EndsWith(" ¥", StringComparison.Ordinal)
            && decimal.TryParse(displayedFraction[..^2], NumberStyles.Number, copy.DisplayCulture, out decimal parsedFraction)
            && parsedFraction == 0.125m, "Visible purchase prices must not round away Core precision.");
        var beforeStaging = CompletionFiles(runtime);
        var gearChoice = MinimalVisible(page).OfType<Button>().First(button => button.IsEnabled
            && button.AutomationId?.StartsWith("creation-gear-catalog-", StringComparison.Ordinal) == true);
        await ClickCompletionControlAsync(ui, gearChoice);
        var stagedGear = new Dictionary<string, int>((Dictionary<string, int>)typeof(CreationGearPage)
            .GetField("_basket", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!, StringComparer.Ordinal);
        Require(stagedGear.Count > 0, "Actual Gear control did not stage a local selection.");
        AssertCompletionFilesUnchanged(runtime, beforeStaging);
        string[] kinds = scenario == "manual-weapon" ? [CharacterCreationLoadoutKinds.Weapon]
            : scenario == "manual-armor" ? [CharacterCreationLoadoutKinds.Armor]
            : [CharacterCreationLoadoutKinds.Weapon, CharacterCreationLoadoutKinds.Armor];
        int expectedPurchases = 0;
        foreach (string kind in kinds)
        {
            var loaded = runtime.Services.GetRequiredService<IOwnerBoundCharacterCreationLoadoutService>()
                .Prepare(owners.Capture(), runtime.Id);
            Require(loaded.Value is not null, "SETUP: actual manual loadout authority failed: " + string.Join(",", loaded.Blockers));
            var option = loaded.Value!.Inputs.Authority.Options.Where(row => row.Kind == kind
                && row.IsSelectable && row.PricingIsExact && row.AvailabilityIsExact && row.Blockers.Count == 0 && row.UnitCost > 0)
                .OrderBy(row => row.UnitCost).ThenBy(row => row.OptionId, StringComparer.Ordinal).First();
            CompletionPageControl<Picker>(page, "creation-loadout-type").SelectedIndex = kind == CharacterCreationLoadoutKinds.Weapon ? 0 : 1;
            var search = CompletionPageControl<SearchBar>(page, "creation-loadout-search");
            search.Text = option.Name;
            ((ISearchBarController)search).OnSearchButtonPressed();
            string token = new(option.OptionId.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-').ToArray());
            var files = CompletionFiles(runtime);
            ((IButtonController)CompletionPageControl<Button>(page, "creation-loadout-add-" + token)).SendClicked();
            if (kind == CharacterCreationLoadoutKinds.Armor)
                Require(!MinimalVisible(page).Any(row => row.AutomationId?.StartsWith("creation-loadout-add-", StringComparison.Ordinal) == true),
                    "The one-armor Core lane must not offer an impossible second armor or quantity.");
            ((IButtonController)CompletionPageControl<Button>(page, "creation-loadout-remove-" + token)).SendClicked();
            AssertCompletionFilesUnchanged(runtime, files);
            ((IButtonController)CompletionPageControl<Button>(page, "creation-loadout-add-" + token)).SendClicked();
            await ClickCompletionControlAsync(ui, CompletionPageControl<Button>(page, "creation-loadout-preview"));
            var canceledConfirm = CompletionPageControl<Button>(page, "creation-loadout-confirm");
            ((IButtonController)CompletionPageControl<Button>(page, "creation-loadout-cancel")).SendClicked();
            ((IButtonController)canceledConfirm).SendClicked();
            await ui.DrainDispatchedAsyncVoidAsync();
            Require(probe.ConfirmCalls == expectedPurchases, "A canceled native review dispatched a purchase.");
            AssertCompletionFilesUnchanged(runtime, files);
            await ClickCompletionControlAsync(ui, CompletionPageControl<Button>(page, "creation-loadout-preview"));
            var prepared = (CharacterCreationLoadoutPreparedPreview)typeof(CreationGearPage)
                .GetField("_loadoutPrepared", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
            var priorGear = ((CharacterCreationGearInteractionLoadResult)typeof(CreationGearPage)
                .GetField("_loaded", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!).State!;
            var confirm = CompletionPageControl<Button>(page, "creation-loadout-confirm");
            if (scenario == "manual-lost-reply" && expectedPurchases == 0)
            {
                var retainedIncrement = MinimalVisible(page).OfType<Button>().First(button =>
                    button.AutomationId?.StartsWith("creation-gear-basket-", StringComparison.Ordinal) == true
                    && button.AutomationId.EndsWith("-increment", StringComparison.Ordinal));
                Task pending = ui.BeginAsyncVoid(() => ((IButtonController)confirm).SendClicked());
                try
                {
                    await probe.ConfirmCommitted.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    ((IButtonController)retainedIncrement).SendClicked();
                    var busyBasket = (Dictionary<string, int>)typeof(CreationGearPage)
                        .GetField("_basket", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
                    Require(stagedGear.Count == busyBasket.Count && stagedGear.All(row => busyBasket.GetValueOrDefault(row.Key) == row.Value),
                        "A retained quantity control changed Gear while the purchase was unresolved.");
                    // Deliberately overlap real lifecycle events with the pending
                    // action; the async-only click adapter is not a lifecycle lock.
                    IssuedPageLifecycle(page, "OnDisappearing");
                    IssuedPageLifecycle(page, "OnAppearing");
                    await Task.Yield();
                }
                finally { probe.ContinueConfirm.TrySetResult(); }
                await JoinIssuedPageAsync(pending);
                await ui.DrainDispatchedAsyncVoidAsync();
            }
            else await ClickCompletionControlAsync(ui, confirm);
            expectedPurchases++;
            if (scenario == "manual-refresh-fault" && expectedPurchases == 1)
            {
                Require(typeof(CreationGearPage).GetField("_loadoutReceipt", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)
                        is CharacterCreationLoadoutReceipt
                    && !MinimalVisible(page).Any(row => row.AutomationId is "creation-loadout-confirm" or "creation-loadout-preview")
                    && MinimalVisibleText(page).Contains(CreationFlowStrings.Get("Loadout.SavedReopen", "missing"), StringComparison.Ordinal),
                    "A failed post-commit refresh lost the durable receipt or offered another purchase.");
                IssuedPageLifecycle(page, "OnDisappearing");
                using (var alerts = new IssuedPageAlerts(page, page.GetParentWindow()))
                {
                    await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing")));
                    Require(alerts.Messages.Count == 1 && probe.ConfirmCalls == expectedPurchases
                        && !MinimalVisible(page).Any(row => row.AutomationId is "creation-loadout-confirm" or "creation-loadout-preview"),
                        "A second failed read must keep the saved receipt without retrying the purchase.");
                }
                IssuedPageLifecycle(page, "OnDisappearing");
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing")));
                Require(!MinimalVisibleText(page).Contains(CreationFlowStrings.Get("Loadout.SavedReopen", "missing"), StringComparison.Ordinal),
                    "A recovered basket must not keep telling the player to reopen it.");
            }
            if (scenario == "manual-lost-reply" && expectedPurchases == 1)
            {
                Require(MinimalVisible(page).Any(row => row.AutomationId == "creation-loadout-recover")
                    && !MinimalVisible(page).Any(row => row.AutomationId == "creation-loadout-confirm"),
                    "A lost reply offered another Confirm instead of receipt recovery.");
                IssuedPageLifecycle(page, "OnDisappearing");
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing")));
                Require(MinimalVisible(page).Any(row => row.AutomationId == "creation-loadout-recover")
                    && !MinimalVisible(page).Any(row => row.AutomationId is "creation-loadout-confirm" or "creation-gear-preview")
                    && probe.ConfirmCalls == expectedPurchases,
                    "Re-entry must preserve receipt-only recovery without exposing another mutation.");
                await ClickCompletionControlAsync(ui, CompletionPageControl<Button>(page, "creation-loadout-recover"));
            }
            var saved = new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!;
            Require(probe.ConfirmCalls == expectedPurchases
                && saved.ContentRevision == before.ContentRevision + expectedPurchases
                && saved.Document.Content == before.Document.Content
                && saved.Document.AuxiliaryState.CharacterCreationLoadoutReceipts?.Count == expectedPurchases,
                "Manual purchase must save one genuine auxiliary receipt without changing XML.");
            var receipt = saved.Document.AuxiliaryState.CharacterCreationLoadoutReceipts!.Last();
            Require(receipt.Draft.Lines.Single(row => row.OptionId == option.OptionId).UnitCost == option.UnitCost
                && receipt.PreviewDigest == prepared.Preview.PreviewDigest,
                "Manual purchase lost the reviewed Core identity or price.");
            var loadout = (CharacterCreationLoadoutState)typeof(CreationGearPage)
                .GetField("_loadoutState", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
            var gear = (CharacterCreationGearInteractionLoadResult)typeof(CreationGearPage)
                .GetField("_loaded", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
            var budget = (CharacterCreationResourcesBudget)typeof(CreationGearPage)
                .GetField("_sharedPurchaseBudget", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
            Require(loadout.Inputs.Binding.ContentRevision == saved.ContentRevision
                && gear.State!.Binding.ContentRevision == saved.ContentRevision
                && budget.RemainingNuyen == prepared.Preview.RemainingNuyen,
                "A purchase did not refresh both baskets and the shared Core budget.");
            AssertManualGearPreservationBoundary(priorGear, gear.State!, loadout, receipt);
            var retainedStagedGear = (Dictionary<string, int>)typeof(CreationGearPage)
                .GetField("_basket", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
            Require(stagedGear.Count == retainedStagedGear.Count
                && stagedGear.All(row => retainedStagedGear.TryGetValue(row.Key, out int quantity) && quantity == row.Value)
                && MinimalVisible(page).Any(row => row.AutomationId == "creation-gear-preview"),
                "Saving weapons or armor discarded the user's unsaved Gear selections.");
        }
        IssuedPageLifecycle(page, "OnDisappearing");
        await navigation.PopAsync(false);
        await ui.DrainDispatchedAsyncVoidAsync();
        var reopened = new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!;
        await HydrateFinalizationOwnerAsync(runtime, owners, reopened);
        await ui.DrainDispatchedAsyncVoidAsync();
        await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(root, "OnAppearing")));
        await WaitForManualGearRouteAsync(ui, root);
        await ClickCompletionControlAsync(ui, CompletionPageControl<Button>(root, "creation-stage-gear"));
        var reopenedPage = (CreationGearPage)navigation.NavigationStack.Last();
        IssuedPageLifecycle(root, "OnDisappearing");
        await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(reopenedPage, "OnAppearing")));
        Require(MinimalVisible(reopenedPage).Count(row => row.AutomationId?.StartsWith("creation-loadout-retained-", StringComparison.Ordinal) == true)
                == kinds.Length
            && !MinimalVisible(reopenedPage).Any(row => row.AutomationId?.StartsWith("creation-loadout-remove-", StringComparison.Ordinal) == true),
            "Reopened saved entries must be retained, without editable saved-row controls.");
        IssuedPageLifecycle(reopenedPage, "OnDisappearing");
        await navigation.PopAsync(false);
        await ui.DrainDispatchedAsyncVoidAsync();
        await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(root, "OnAppearing")));
        await ui.DrainDispatchedAsyncVoidAsync();
        return reopened;
    }

    private static async Task WaitForManualGearRouteAsync(IssuedPageUiContext ui, BuildPage page)
    {
        // The dashboard publishes each real authority phase asynchronously.
        // Wait for its actual route; do not inject a ready flag or render a synthetic stage.
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            await ui.DrainDispatchedAsyncVoidAsync();
            if (MinimalVisible(page).OfType<Button>().Any(row => row.AutomationId == "creation-stage-gear" && row.IsEnabled)) return;
            await Task.Delay(25);
        }
        throw new InvalidOperationException("Actual saved Resources never admitted the native Gear route.");
    }

    private static void AssertManualGearPreservationBoundary(CharacterCreationGearInteractionState previous,
        CharacterCreationGearInteractionState current, CharacterCreationLoadoutState loadout,
        CharacterCreationLoadoutReceipt receipt)
    {
        // Exercise the pure carry-forward predicate with genuine observed states.
        // Counterexamples are never installed into the page or persisted to Core.
        var check = typeof(CreationGearPage).GetMethod("CanPreserveStagedGearForPurchaseRefresh",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        bool Allows(CharacterCreationGearInteractionState next, CharacterCreationLoadoutState state,
            CharacterCreationLoadoutReceipt? proof) => (bool)check.Invoke(null,
                [previous.SnapshotDigest, previous, next, state, proof])!;
        Require(Allows(current, loadout, receipt), "Exact saved purchase must permit basket carry-forward.");
        Require(!Allows(current, loadout, null)
            && !Allows(current, loadout, receipt with { PreviousContentRevision = receipt.PreviousContentRevision - 1 })
            && !Allows(current, loadout, receipt with { PreviousSavedRevision = receipt.PreviousSavedRevision - 1 })
            && !Allows(current with { Binding = current.Binding with { ContentRevision = current.Binding.ContentRevision + 1 } }, loadout, receipt)
            && !Allows(current with { Binding = current.Binding with { SavedRevision = current.Binding.SavedRevision + 1 } }, loadout, receipt)
            && !Allows(current, loadout with { Inputs = loadout.Inputs with { PendingDraft = null } }, receipt)
            && !Allows(current, loadout with { Inputs = loadout.Inputs with { Receipts = [] } }, receipt),
            "An old, missing, mismatched or unrelated purchase cannot preserve staged Gear.");
    }

    private static void AssertManualLoadoutProposal(CharacterCreationCompletionReviewInputs inputs,
        CompletionAssistantProposal proposal, CharacterCreationLoadoutDraft manual)
    {
        Require(inputs.Loadout?.PendingDraft?.DraftDigest == manual.DraftDigest
            && inputs.Loadout.Receipts?.Count > 0 && proposal.Loadout is not null,
            "Finish did not read the actual saved manual loadout and receipt.");
        foreach (var line in manual.Lines)
        {
            var retained = proposal.Loadout!.Lines.Single(row => row.OptionId == line.OptionId);
            Require(retained.LineDigest == line.LineDigest && retained.Quantity == line.Quantity
                && retained.TotalCost == line.TotalCost && retained.SourceId == line.SourceId,
                "Finish repriced or duplicated a saved manual choice.");
        }
        Require(proposal.Request.Purchases.Count(row => row.Weapon is not null) == (manual.Selections.Weapons.Count == 0 ? 1 : 0)
            && proposal.Request.Purchases.Count(row => row.Armor is not null) == (manual.Selections.Armor.Count == 0 ? 1 : 0)
            && proposal.Loadout!.PurchaseCost == proposal.Loadout.Lines.Sum(row => row.TotalCost),
            "Missing-only defaults must fill only absent weapon/armor categories and count retained costs once.");
    }

    private static void AssertFinalManualLoadout(WorkspaceStoredDocument saved, CompletionAssistantProposal proposal,
        CharacterCreationLoadoutDraft manual)
    {
        var root = XElement.Parse(saved.Document.Content);
        var weapons = root.Element("weapons")?.Elements("weapon").ToArray() ?? [];
        var armor = root.Element("armors")?.Elements("armor").ToArray() ?? [];
        Require(weapons.Length == proposal.Loadout!.Selections.Weapons.Sum(row => row.Quantity)
            && armor.Length == proposal.Loadout.Selections.Armor.Sum(row => row.Quantity),
            "Final Apply duplicated weapon or armor rows.");
        foreach (var line in manual.Lines)
        {
            var rows = (line.Kind == CharacterCreationLoadoutKinds.Weapon ? weapons : armor)
                .Where(row => row.Element("sourceid")?.Value == line.SourceId.ToString("D")).ToArray();
            var ids = manual.Instances.Single(row => row.OptionId == line.OptionId).InstanceIds;
            Require(rows.Length == line.Quantity
                && rows.Select(row => Guid.Parse(row.Element("guid")!.Value)).Order().SequenceEqual(ids.Order())
                && rows.All(row => decimal.Parse(row.Element("cost")!.Value, CultureInfo.InvariantCulture) == line.UnitCost),
                "Final Apply changed saved manual instance identities or exact Core costs.");
        }
    }

    private sealed class ManualLoadoutServiceProbe(IOwnerBoundCharacterCreationLoadoutService inner,
        IssuedPageUiContext ui) : IOwnerBoundCharacterCreationLoadoutService
    {
        public bool LoseReply;
        public bool FailRefresh;
        public int ConfirmCalls;
        public TaskCompletionSource ConfirmCommitted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ContinueConfirm { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _loseLookup;
        private int _failedReadsRemaining;
        public CharacterCreationFoundationResult<CharacterCreationLoadoutState> Prepare(OwnerContextStamp owner, CharacterWorkspaceId id, CancellationToken ct = default)
        {
            Require(SynchronizationContext.Current != ui, "Manual preparation blocked the UI.");
            if (_failedReadsRemaining > 0) { _failedReadsRemaining--; throw new IOException("synthetic-post-commit-loadout-read"); }
            return inner.Prepare(owner, id, ct);
        }
        public CharacterCreationFoundationResult<CharacterCreationLoadoutPreview> Preview(OwnerContextStamp owner, CharacterCreationLoadoutPreviewRequest request, CancellationToken ct = default)
        { Require(SynchronizationContext.Current != ui, "Manual preview blocked the UI."); return inner.Preview(owner, request, ct); }
        public CharacterCreationFoundationResult<CharacterCreationLoadoutReceipt> Confirm(OwnerContextStamp owner, CharacterCreationLoadoutConfirmRequest request, CancellationToken ct = default)
        {
            Require(SynchronizationContext.Current != ui, "Manual confirmation blocked the UI.");
            ConfirmCalls++;
            var result = inner.Confirm(owner, request, ct);
            if (FailRefresh && result.Value is not null) { FailRefresh = false; _failedReadsRemaining = 2; }
            if (LoseReply && result.Value is not null)
            {
                LoseReply = false;
                _loseLookup = true;
                ConfirmCommitted.TrySetResult();
                Require(ContinueConfirm.Task.Wait(TimeSpan.FromSeconds(10)), "SETUP: in-flight purchase observation was not released.");
                throw new IOException("synthetic-lost-loadout-reply");
            }
            return result;
        }
        public CharacterCreationFoundationResult<CharacterCreationLoadoutReceipt> LookupReceipt(OwnerContextStamp owner, CharacterWorkspaceId id, string key)
        {
            if (_loseLookup) { _loseLookup = false; throw new IOException("synthetic-loadout-lookup-unavailable"); }
            return inner.LookupReceipt(owner, id, key);
        }
    }
}
