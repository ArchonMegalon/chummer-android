using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using Chummer.Android.Native;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Chummer.Presentation.Creation;
using Chummer.Presentation.Overview;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    private const string ExactPartialManualScenario = "manual-exact-partial-preview239";
    private const string CapturedPartialManualScenario = "manual-captured-partial-preview239";
    private const string CapturedPartialManualSha256 = "ee0b248578ce0a21781ea9c02d74de284b57d83dc25f5b49c3d69261fc39addf";
    private const string CapturedPartialManualWorkspaceId = "34793a6ab45e4e89beec750faaf0551d";
    private const string ExactPartialWeapon = "weapon:b625833a-88bc-4047-b3fc-bc432d96744b";
    private const string ExactPartialArmor = "armor:40826eaa-c22a-43da-8730-bc1867ea65a1";

    // Explicit scenarioFilter only: the ordinary suite never needs an external capture.
    private static async Task RunCapturedPartialManualCompletionAsync(IssuedPageUiContext ui,
        NativeRewardRuntime runtime, ControlledLinkedOwner owners, Window window,
        CompletionServiceProbe service, ManualLoadoutServiceProbe loadoutService)
    {
        string stage = "captured-fixture";
        string? source = null;
        string? copy = null;
        byte[]? original = null;
        BuildPage? root = null;
        try
        {
            source = Environment.GetEnvironmentVariable("CHUMMER_COMPLETION_CAPTURED_WORKSPACE");
            string? expectedHash = Environment.GetEnvironmentVariable("CHUMMER_COMPLETION_CAPTURED_WORKSPACE_SHA256");
            Require(!string.IsNullOrWhiteSpace(source) && File.Exists(source),
                "stage=captured-fixture; CHUMMER_COMPLETION_CAPTURED_WORKSPACE-missing");
            Require(string.Equals(expectedHash, CapturedPartialManualSha256, StringComparison.OrdinalIgnoreCase),
                "stage=captured-fixture; CHUMMER_COMPLETION_CAPTURED_WORKSPACE_SHA256-missing-or-unexpected");
            original = File.ReadAllBytes(source!);
            Require(string.Equals(Convert.ToHexString(SHA256.HashData(original)), CapturedPartialManualSha256,
                StringComparison.OrdinalIgnoreCase), "stage=captured-fixture; sha256-mismatch");
            // This is FileWorkspaceStore's persisted record, NOT WorkspaceStoredDocument JSON.
            // Inspect only the routing/schema; the real store must deserialize and admit it.
            using var json = JsonDocument.Parse(original);
            var record = json.RootElement;
            Require(record.GetProperty("RecordSchemaVersion").GetInt32() == 4
                && record.GetProperty("Format").GetString() == "NativeXml"
                && record.GetProperty("Envelope").GetProperty("PayloadKind").GetString() == "sr5/chum5-xml"
                && record.GetProperty("AuxiliaryState").GetProperty("CharacterCreationBootstrapBinding")
                    .GetProperty("WorkspaceId").GetProperty("Value").GetString() == CapturedPartialManualWorkspaceId,
                "stage=captured-fixture; persisted-record-schema-or-identity-mismatch");
            Require(owners.Capture().Owner == OwnerScope.LocalSingleUser,
                "stage=captured-fixture; local-test-owner-required");
            stage = "captured-runtime-initialization";
            await runtime.Coordinator.InitializeAsync();
            await AccountStartupTask(runtime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
            stage = "captured-copy";
            runtime.Id = new(CapturedPartialManualWorkspaceId);
            // The capture has no owner/adoption claim. Preserve every recorded byte, including
            // history and authority witnesses, under the existing local owner's private store.
            string target = Path.Combine(runtime.StateDirectory, "workspaces", runtime.Id.Value + ".json");
            Require(!File.Exists(target), "stage=captured-copy; target-already-exists");
            File.Copy(source!, target, overwrite: false);
            copy = target;
            File.SetLastWriteTimeUtc(copy, File.GetLastWriteTimeUtc(source!));
            AssertCapturedPartialManualBytes(source!, copy, original);

            stage = "captured-store-read";
            var read = new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id);
            Require(read.Success, "stage=captured-store-read; outcome=" + read.Outcome);
            var saved = read.Value!;
            AssertCapturedPartialManualCheckpoint(saved);
            var files = CompletionFiles(runtime);
            Console.WriteLine("TRACE " + CapturedPartialManualScenario + "; stage=" + stage + "; outcome=" + read.Outcome);

            stage = "captured-owner-hydration";
            await HydrateFinalizationOwnerAsync(runtime, owners, saved, contentFreeFailure: true);
            Console.WriteLine("TRACE " + CapturedPartialManualScenario + "; stage=" + stage + "; current=true");
            var hydratedFiles = CompletionFiles(runtime);
            // Real selection persists global settings during fixture setup. Establish the
            // read-only page baseline afterward, without excusing any workspace-file change.
            string settingsPath = files.Keys.Single(path => Path.GetFileName(path) == "global-settings.json");
            Require(hydratedFiles.Count == files.Count && files.All(pair =>
                    hydratedFiles.TryGetValue(pair.Key, out string? hash)
                    && (pair.Key == settingsPath || pair.Value == hash)),
                "stage=captured-owner-hydration; unexpected-file-change");
            AssertCapturedPartialManualBytes(source!, copy, original);
            files = hydratedFiles;

            stage = "captured-build-page";
            root = new BuildPage(runtime.Coordinator,
                new CharacterCreationResourcesInteractionPresenter(
                    runtime.Services.GetRequiredService<ICharacterCreationResourcesService>(),
                    runtime.Services.GetRequiredService<IOwnerBoundCharacterCreationResourcesService>()),
                runtime.Presenter,
                new CharacterCreationGearInteractionPresenter(
                    runtime.Services.GetRequiredService<ICharacterCreationGearService>(),
                    runtime.Services.GetRequiredService<IOwnerBoundCharacterCreationGearService>()));
            var rootScroll = (IScrollViewController)(ScrollView)root.Content!;
            rootScroll.ScrollToRequested += (_, _) => rootScroll.SendScrollFinished();
            var shell = new Shell();
            shell.Items.Add(new ShellContent { Content = root });
            window.Page = shell;
            await ui.DrainDispatchedAsyncVoidAsync();
            Require(ReferenceEquals(Shell.Current?.CurrentPage, root), "stage=captured-build-page; root-not-current");
            IssuedPageLifecycle(root, "OnDisappearing");
            await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(root, "OnAppearing")));
            await ui.DrainDispatchedAsyncVoidAsync();
            AssertCompletionFilesUnchanged(runtime, files);

            stage = "captured-preview-cancel";
            await RunExactPartialManualCompletionAsync(ui, runtime, owners, root, window, saved, files, service,
                CapturedPartialManualScenario);
            var reopened = new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id);
            Require(reopened.Success, "stage=captured-cancel-reopen; outcome=" + reopened.Outcome);
            AssertCapturedPartialManualCheckpoint(reopened.Value!);
            Require(FinalizationDocumentDigest(reopened.Value!) == FinalizationDocumentDigest(saved)
                && JsonSerializer.Serialize(reopened.Value!.LocalHistory) == JsonSerializer.Serialize(saved.LocalHistory)
                && reopened.Value.LastUpdatedUtc == saved.LastUpdatedUtc,
                "stage=captured-cancel-reopen; document-or-history-changed");
            AssertCompletionFilesUnchanged(runtime, files);
        }
        catch (Exception exception)
        {
            Console.WriteLine("FAIL " + CapturedPartialManualScenario + "; stage=" + stage
                + "; exception-type=" + exception.GetType().FullName);
            // Method identities only: no exception messages, arguments, locals or source content.
            Console.WriteLine("TRACE " + CapturedPartialManualScenario + "; failure-methods="
                + string.Join(" > ", (new System.Diagnostics.StackTrace(exception, false).GetFrames() ?? [])
                    .Take(10).Select(frame => frame.GetMethod())
                    .Select(method => method?.DeclaringType?.FullName + "." + method?.Name)));
            if (root?.Navigation.NavigationStack.OfType<CreationCompletionPage>().LastOrDefault() is { } page)
            {
                Console.WriteLine("TRACE " + CapturedPartialManualScenario + "; native-load="
                    + JsonSerializer.Serialize(page.LoadObservations));
                Console.WriteLine("TRACE " + CapturedPartialManualScenario + "; native-preview="
                    + JsonSerializer.Serialize(page.PreviewObservations));
            }
            if (exception is InvalidOperationException && exception.Message.StartsWith("stage=", StringComparison.Ordinal))
                throw;
            // Do not include arbitrary exception messages/inner exceptions carrying source content.
            throw new InvalidOperationException("stage=" + stage + "; exception-type=" + exception.GetType().FullName);
        }
        finally
        {
            // Check even rejected pre-choice reads. Never repair or reserialize an inadmissible capture.
            if (copy is not null) AssertCapturedPartialManualBytes(source!, copy, original!);
            Require(loadoutService.ConfirmCalls == 0 && service.ReviewCalls == 0 && service.ApplyCalls == 0,
                "stage=captured-nonmutation; purchase-review-or-apply-dispatched");
        }
        Console.WriteLine("PASS actual Core/MAUI completion page: " + CapturedPartialManualScenario);
    }

    private static void AssertCapturedPartialManualBytes(string source, string copy, byte[] original)
    {
        foreach (var (path, label) in new[] { (source, "source"), (copy, "copy") })
        {
            bool unchanged;
            try { unchanged = File.ReadAllBytes(path).SequenceEqual(original); }
            catch (Exception exception)
            {
                throw new InvalidOperationException("stage=captured-nonmutation; " + label
                    + "-read-exception=" + exception.GetType().FullName);
            }
            Require(unchanged, "stage=captured-nonmutation; " + label + "-bytes-changed");
        }
    }

    private static void AssertCapturedPartialManualCheckpoint(WorkspaceStoredDocument saved)
    {
        AssertExactPartialManualCheckpoint(saved, purchased: true);
        var manual = saved.Document.AuxiliaryState.CharacterCreationLoadoutDraft!;
        var receipt = saved.Document.AuxiliaryState.CharacterCreationLoadoutReceipts!.Single();
        Require(saved.Id.Value == CapturedPartialManualWorkspaceId
            && receipt.PreviousContentRevision == 3 && receipt.PreviousSavedRevision == 3
            && receipt.ContentRevision == 4 && receipt.SavedRevision == 4
            && manual.Instances.Single(row => row.OptionId == ExactPartialWeapon).InstanceIds
                .SequenceEqual(new[] { Guid.Parse("c0d2e91f-dd11-b7ee-88f9-82825ddbe59b") })
            && manual.Instances.Single(row => row.OptionId == ExactPartialArmor).InstanceIds
                .SequenceEqual(new[] { Guid.Parse("9b3bdbb1-f7d4-f0df-ff0a-48903f66669c") }),
            "stage=captured-checkpoint; workspace-receipt-or-instance-identity-mismatch");
    }

    private static async Task<WorkspaceStoredDocument> PurchaseManualLoadoutThroughGearAsync(
        IssuedPageUiContext ui, NativeRewardRuntime runtime, ControlledLinkedOwner owners, BuildPage root,
        WorkspaceStoredDocument before, string scenario, ManualLoadoutServiceProbe probe)
    {
        if (scenario == ExactPartialManualScenario)
            return await PurchaseExactPartialManualLoadoutAsync(ui, runtime, owners, root, before, probe);
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

    private static async Task<WorkspaceStoredDocument> PurchaseExactPartialManualLoadoutAsync(
        IssuedPageUiContext ui, NativeRewardRuntime runtime, ControlledLinkedOwner owners, BuildPage root,
        WorkspaceStoredDocument before, ManualLoadoutServiceProbe probe)
    {
        AssertExactPartialManualCheckpoint(before, purchased: false);
        await WaitForManualGearRouteAsync(ui, root);
        await ClickCompletionControlAsync(ui, ExactPartialManualControl<Button>(root, "creation-stage-gear", "gear-open"));
        Require(root.Navigation.NavigationStack.Last() is CreationGearPage, "stage=gear-open; page-missing");
        var page = (CreationGearPage)root.Navigation.NavigationStack.Last();
        IssuedPageLifecycle(root, "OnDisappearing");
        await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing")));
        var state = (CharacterCreationLoadoutState?)typeof(CreationGearPage)
            .GetField("_loadoutState", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page);
        Require(state is not null, "stage=gear-load; loadout-authority-missing");
        var files = CompletionFiles(runtime);
        foreach (var (id, kind, price, index) in new[]
        {
            (ExactPartialWeapon, CharacterCreationLoadoutKinds.Weapon, 950m, 0),
            (ExactPartialArmor, CharacterCreationLoadoutKinds.Armor, 450m, 1)
        })
        {
            var option = state!.Inputs.Authority.Options.SingleOrDefault(row => row.OptionId == id);
            Require(option is not null && option.Kind == kind && option.UnitCost == price
                && option.IsSelectable && option.PricingIsExact && option.AvailabilityIsExact && option.Blockers.Count == 0,
                "stage=gear-selection; exact-typed-option-unavailable; blockers=" + string.Join(",", option?.Blockers ?? []));
            ExactPartialManualControl<Picker>(page, "creation-loadout-type", "gear-selection").SelectedIndex = index;
            var search = ExactPartialManualControl<SearchBar>(page, "creation-loadout-search", "gear-selection");
            search.Text = option!.Name;
            ((ISearchBarController)search).OnSearchButtonPressed();
            string token = new(id.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-').ToArray());
            ((IButtonController)ExactPartialManualControl<Button>(page, "creation-loadout-add-" + token, "gear-selection")).SendClicked();
        }
        // One combined preview/confirmation. No unrelated Gear basket or empty
        // allocation/quality confirmation is allowed to change this checkpoint.
        AssertCompletionFilesUnchanged(runtime, files);
        await ClickCompletionControlAsync(ui, ExactPartialManualControl<Button>(page, "creation-loadout-preview", "gear-preview"));
        var prepared = (CharacterCreationLoadoutPreparedPreview?)typeof(CreationGearPage)
            .GetField("_loadoutPrepared", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page);
        Require(prepared is not null && prepared.Preview.RemainingNuyen == 138600m && probe.ConfirmCalls == 0,
            "stage=gear-preview; combined-preview-or-budget-mismatch");
        AssertCompletionFilesUnchanged(runtime, files);
        await ClickCompletionControlAsync(ui, ExactPartialManualControl<Button>(page, "creation-loadout-confirm", "gear-confirm"));
        var saved = new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!;
        AssertExactPartialManualCheckpoint(saved, purchased: true);
        var receipt = saved.Document.AuxiliaryState.CharacterCreationLoadoutReceipts!.Single();
        Require(probe.ConfirmCalls == 1 && saved.Document.Content == before.Document.Content
            && receipt.PreviousContentRevision == 3 && receipt.PreviousSavedRevision == 3
            && receipt.ContentRevision == 4 && receipt.SavedRevision == 4
            && receipt.PreviewDigest == prepared!.Preview.PreviewDigest,
            "stage=gear-confirm; combined-receipt-or-revision-mismatch");

        IssuedPageLifecycle(page, "OnDisappearing");
        await root.Navigation.PopAsync(false);
        await ui.DrainDispatchedAsyncVoidAsync();
        var committedFiles = CompletionFiles(runtime);
        var reopened = new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!;
        await HydrateFinalizationOwnerAsync(runtime, owners, reopened, contentFreeFailure: true);
        await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(root, "OnAppearing")));
        await ui.DrainDispatchedAsyncVoidAsync();
        await WaitForManualGearRouteAsync(ui, root);
        AssertExactPartialManualCheckpoint(reopened, purchased: true);
        Require(JsonSerializer.Serialize(reopened.Document.AuxiliaryState.CharacterCreationLoadoutReceipts!.Single())
                == JsonSerializer.Serialize(receipt), "stage=reopen; manual-receipt-changed");
        AssertCompletionFilesUnchanged(runtime, committedFiles);
        return reopened;
    }

    private static void AssertExactPartialManualCheckpoint(WorkspaceStoredDocument stored, bool purchased)
    {
        var auxiliary = stored.Document.AuxiliaryState;
        var prerequisite = auxiliary.CharacterCreationPrerequisiteDraft;
        var resources = auxiliary.CharacterCreationResourcesDraft;
        var expectedRanks = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [CharacterCreationPriorityCategoryIds.Heritage] = "D",
            [CharacterCreationPriorityCategoryIds.Talent] = "E",
            [CharacterCreationPriorityCategoryIds.Attributes] = "A",
            [CharacterCreationPriorityCategoryIds.Skills] = "B",
            [CharacterCreationPriorityCategoryIds.Resources] = "C"
        };
        Require(prerequisite is not null && prerequisite.BuildMethod == CharacterCreationBuildMethods.Priority
            && prerequisite.HeritageSelection?.MetatypeName == "Human"
            && prerequisite.TalentSelection?.Value.Equals(CharacterCreationMagicResonanceKinds.Mundane, StringComparison.OrdinalIgnoreCase) == true
            && prerequisite.Assignments.Count == expectedRanks.Count
            && prerequisite.Assignments.All(row => expectedRanks.GetValueOrDefault(row.CategoryId) == row.Rank),
            "stage=checkpoint; exact-partial-prerequisite-mismatch");
        // Check the serialized auxiliary contract without depending on the names
        // of absent allocation drafts. A saved empty draft is not an absent draft.
        string[] expectedDrafts = purchased
            ? ["CharacterCreationPrerequisiteDraft", "CharacterCreationResourcesDraft", "CharacterCreationLoadoutDraft"]
            : ["CharacterCreationPrerequisiteDraft", "CharacterCreationResourcesDraft"];
        var drafts = JsonSerializer.SerializeToElement(auxiliary).EnumerateObject()
            .Where(row => row.Name.EndsWith("Draft", StringComparison.Ordinal) && row.Value.ValueKind != JsonValueKind.Null)
            .Select(row => row.Name).OrderBy(name => name, StringComparer.Ordinal);
        Require(drafts.SequenceEqual(expectedDrafts.OrderBy(name => name, StringComparer.Ordinal))
            && stored.ContentRevision == (purchased ? 4 : 3) && stored.SavedRevision == stored.ContentRevision
            && resources is { KarmaInvestment: 0, Budget: { PriorityNuyen: 140000m, KarmaInvestment: 0,
                TotalStartingNuyen: 140000m, NuyenFromKarma: 0, IsExact: true } }
            && auxiliary.CharacterCreationResourcesReceipts?.Count == 1
            && auxiliary.CharacterCreationFinalizationReceipts is null,
            "stage=checkpoint; absent-drafts-funding-or-revisions-mismatch");
        if (!purchased)
        {
            Require(auxiliary.CharacterCreationLoadoutDraft is null && auxiliary.CharacterCreationLoadoutReceipts is null,
                "stage=checkpoint; unexpected-prior-purchase");
            return;
        }
        var manual = auxiliary.CharacterCreationLoadoutDraft
            ?? throw new InvalidOperationException("stage=checkpoint; manual-draft-missing");
        Require(manual.Lines.Count == 2 && manual.PurchaseCost == 1400m
            && manual.Selections.Weapons.Count == 1 && manual.Selections.Weapons[0].OptionId == ExactPartialWeapon
            && manual.Selections.Weapons[0].Quantity == 1
            && manual.Selections.Armor.Count == 1 && manual.Selections.Armor[0].OptionId == ExactPartialArmor
            && manual.Selections.Armor[0].Quantity == 1
            && auxiliary.CharacterCreationLoadoutReceipts?.Count == 1,
            "stage=checkpoint; exact-combined-loadout-mismatch");
        foreach (var (id, kind, price) in new[]
        {
            (ExactPartialWeapon, CharacterCreationLoadoutKinds.Weapon, 950m),
            (ExactPartialArmor, CharacterCreationLoadoutKinds.Armor, 450m)
        })
        {
            var line = manual.Lines.SingleOrDefault(row => row.OptionId == id);
            Require(line is not null && line.Kind == kind && line.SourceId == Guid.Parse(id[(id.IndexOf(':') + 1)..])
                && line.Quantity == 1 && line.UnitCost == price && line.TotalCost == price
                && manual.Instances.Count == 2
                && manual.Instances.Single(row => row.OptionId == id).InstanceIds is { Count: 1 } ids && ids[0] != Guid.Empty,
                "stage=checkpoint; manual-identity-quantity-price-or-instance-mismatch");
        }
        Require(JsonSerializer.Serialize(auxiliary.CharacterCreationLoadoutReceipts!.Single().Draft) == JsonSerializer.Serialize(manual),
            "stage=checkpoint; receipt-does-not-cover-exact-draft");
    }

    private static T ExactPartialManualControl<T>(NativePageBase page, string id, string stage) where T : VisualElement
        => MinimalVisible(page).OfType<T>().SingleOrDefault(row => row.AutomationId == id)
            ?? throw new InvalidOperationException("stage=" + stage + "; control-missing=" + id);

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
