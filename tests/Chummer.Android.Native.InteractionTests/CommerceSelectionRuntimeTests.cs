using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Chummer.Android.Native;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation;
using Chummer.Presentation.Overview;
using Chummer.Presentation.Shell;
using Chummer.Infrastructure.Workspaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Internals;
using Microsoft.Maui.Storage;

internal static class CommerceSelectionRuntimeTests
{
    public static void VerifyProductionOwnerStores()
    {
        // This is the real production adapter and Core store, not the catalog
        // fake below. Equal IDs in two owner namespaces must never alias.
        using var state = new CommerceTestDirectory();
        var store = new FileWorkspaceStore(state.Path);
        var owners = new AfterRunAuthorityHarness.ControlledLinkedOwner();
        var linked = new OwnerScope("commerce-owner-a");
        var id = new CharacterWorkspaceId(Guid.NewGuid().ToString("N"));
        const string localXml = "<character><name>Local synthetic runner</name></character>";
        const string linkedXml = "<character><name>Linked synthetic runner</name></character>";
        Require(store.CreateWorkspaceDocument(id, new(localXml, "sr5")).Success, "Local fixture failed.");
        Require(store.CreateWorkspaceDocument(linked, id, new(linkedXml, "sr5")).Success, "Linked fixture failed.");
        owners.Set(linked);
        using var provider = new ServiceCollection()
            .AddSingleton<IWorkspaceStore>(store)
            .AddSingleton<IOwnerContextAccessor>(owners)
            .AddSingleton<IOwnerContextLeaseAccessor>(owners)
            .AddSingleton<CareerCommerceOwnerAdmission>()
            .AddSingleton<AndroidSr5CareerCyberwareWorkspaceStore>()
            .AddSingleton<AndroidSr5CareerCustomDrugWorkspaceStore>()
            .BuildServiceProvider();
        var admission = provider.GetRequiredService<CareerCommerceOwnerAdmission>();
        var cyberStore = provider.GetRequiredService<AndroidSr5CareerCyberwareWorkspaceStore>();
        var drugStore = provider.GetRequiredService<AndroidSr5CareerCustomDrugWorkspaceStore>();
        Reject(() => cyberStore.Read(id));
        Reject(() => drugStore.Read(id));
        OwnerContextStamp firstOwner = owners.Capture();
        Require(admission.TryEnter(owners.Capture(), id, out var lease), "Owner fixture lease unavailable.");
        Sr5CareerCyberwareWorkspaceSnapshot oldCyber;
        Sr5CareerCustomDrugWorkspaceSnapshot oldDrug;
        using (lease)
        {
            var cyber = cyberStore.Read(id);
            var drug = drugStore.Read(id);
            Require(cyber?.Document.Content == linkedXml && drug?.Document.Content == linkedXml,
                "Production commerce adapters read LocalSingleUser despite an admitted linked owner with the same workspace ID.");
            oldCyber = cyber!;
            oldDrug = drug!;
            Require(!admission.TryEnter(firstOwner, id, out _), "Nested non-reentrant owner lease admitted.");
            Reject(() => cyberStore.Read(new("another-workspace")));
            Task.Run(() => Reject(() => drugStore.Read(id))).GetAwaiter().GetResult();
            Require(cyberStore.ReplaceAndCheckpoint(cyber!, linkedXml.Replace("Linked", "Changed")).Applied,
                "Scoped Cyberware CAS failed.");
            Require(drugStore.ReplaceAndCheckpoint(drugStore.Read(id)!, linkedXml).Applied,
                "Scoped drug CAS failed.");
        }
        Require(store.Get(id).Value!.Document.Content == localXml, "Scoped CAS modified the local runner.");
        var reopened = new FileWorkspaceStore(state.Path);
        Require(reopened.Get(linked, id).Value is { ContentRevision: 3, SavedRevision: 3 } persisted
            && persisted.Document.Content == linkedXml && reopened.Get(id).Value!.Document.Content == localXml,
            "Scoped writes failed exact save/reopen or changed another owner.");
        owners.Set(OwnerScope.LocalSingleUser);
        Require(admission.TryEnter(owners.Capture(), id, out lease), "Trusted local lease failed.");
        using (lease)
        {
            Require(cyberStore.Read(id)?.Document.Content == localXml && drugStore.Read(id)?.Document.Content == localXml,
                "Trusted local route stopped reading legacy workspace.");
            Require(drugStore.ReplaceAndCheckpoint(drugStore.Read(id)!, localXml).Applied,
                "Trusted local atomic checkpoint failed.");
        }
        var other = new OwnerScope("commerce-owner-b");
        Require(store.CreateWorkspaceDocument(other, id, new(linkedXml, "sr5")).Success, "Owner B fixture failed.");
        owners.Set(other);
        Require(admission.TryEnter(owners.Capture(), id, out lease), "Owner B lease failed.");
        using (lease)
        {
            Reject(() => cyberStore.ReplaceAndCheckpoint(oldCyber, localXml));
            Reject(() => drugStore.ReplaceAndCheckpoint(oldDrug, localXml));
        }
        owners.Set(linked);
        Require(!admission.TryEnter(firstOwner, id, out _), "Owner ABA admitted a stale stamp.");
        Require(admission.TryEnter(owners.Capture(), id, out lease), "Restored owner lease failed.");
        using (lease)
        {
            Reject(() => cyberStore.ReplaceAndCheckpoint(oldCyber, localXml));
            Reject(() => drugStore.ReplaceAndCheckpoint(oldDrug, localXml));
        }
        Require(owners.ActiveLeases == 0, "Production stores leaked owner lease.");
        VerifyProductionDrafts(admission, owners, id, linked, other);
        Console.WriteLine("PASS production commerce owner-store isolation.");
    }

    private static void VerifyProductionDrafts(CareerCommerceOwnerAdmission admission,
        AfterRunAuthorityHarness.ControlledLinkedOwner owners, CharacterWorkspaceId id,
        OwnerScope linked, OwnerScope other)
    {
        IPreferences previous = Preferences.Default;
        var settings = new AfterRunAuthorityHarness.RuntimePreferences();
        MethodInfo setter = typeof(Preferences).GetMethod("SetDefault", BindingFlags.Static
            | BindingFlags.Public | BindingFlags.NonPublic)!;
        setter.Invoke(null, [settings]);
        try
        {
            var cyberStore = new PreferencesSr5CareerCyberwarePurchaseCheckpointStore(admission);
            var drugStore = new PreferencesSr5CareerCustomDrugRecipeCheckpointStore(admission);
            Reject(() => cyberStore.Read(id));
            Reject(() => drugStore.Clear(id));
            string digest = new('a', 64);
            var cyber = new Sr5CareerCyberwarePurchaseCheckpoint(Sr5CareerCyberwarePurchaseSchemas.CheckpointV1,
                id, 1, digest, digest, Sr5CareerCyberwarePurchaseService.EmptySelection,
                Sr5CareerCyberwarePurchasePhase.Editing, null, null);
            var drug = new Sr5CareerCustomDrugRecipeCheckpoint(Sr5CareerCustomDrugRecipeSchemas.CheckpointV1,
                id, 1, digest, digest, digest, Sr5CareerCustomDrugRecipeService.EmptySelection,
                Sr5CareerCustomDrugRecipePhase.Editing, null, null);
            owners.Set(OwnerScope.LocalSingleUser);
            Require(admission.TryEnter(owners.Capture(), id, out var scope), "Local draft lease failed.");
            using (scope)
            {
                cyberStore.Write(cyber);
                drugStore.Write(drug);
                string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id.Value))).ToLowerInvariant();
                Require(settings.ContainsKey("chummer.android.sr5-career-cyberware-purchase.v1." + hash)
                    && settings.ContainsKey("chummer.android.sr5-career-custom-drug-recipe.v1." + hash),
                    "Legacy local draft keys changed.");
            }
            owners.Set(linked);
            Require(admission.TryEnter(owners.Capture(), id, out scope), "Linked draft lease failed.");
            using (scope)
            {
                Require(cyberStore.Read(id) is null && drugStore.Read(id) is null, "Linked owner adopted local drafts.");
                cyberStore.Write(cyber with { Selection = cyber.Selection with { MarkupPercent = 42 } });
                drugStore.Write(drug with { Selection = drug.Selection with { Name = "linked draft" } });
            }
            owners.Set(other);
            Require(admission.TryEnter(owners.Capture(), id, out scope), "Other draft lease failed.");
            using (scope)
            {
                Require(cyberStore.Read(id) is null && drugStore.Read(id) is null, "Owner B adopted owner A drafts.");
                cyberStore.Clear(id);
                drugStore.Clear(id);
            }
            owners.Set(linked);
            Require(admission.TryEnter(owners.Capture(), id, out scope), "Linked restore lease failed.");
            using (scope)
            {
                Require(cyberStore.Read(id)?.Selection.MarkupPercent == 42
                    && drugStore.Read(id)?.Selection.Name == "linked draft", "Linked draft failed to restore.");
                cyberStore.Clear(id);
                drugStore.Clear(id);
            }
            owners.Set(OwnerScope.LocalSingleUser);
            Require(admission.TryEnter(owners.Capture(), id, out scope), "Local restore lease failed.");
            using (scope)
                Require(cyberStore.Read(id)?.Selection.MarkupPercent == 0
                    && drugStore.Read(id)?.Selection.Name == "", "Linked clearing damaged local drafts.");
        }
        finally { setter.Invoke(null, [previous]); }
        Require(owners.ActiveLeases == 0, "Draft store leaked owner lease.");
        Console.WriteLine("PASS production preference namespaces and legacy local draft preservation.");
    }

    public static async Task RunOwnerActionsAsync()
    {
        foreach (string change in new[] { "owner", "owner-aba", "forged", "draft", "phase", "command" })
        {
            using var fixture = new Fixture();
            fixture.PrepareDrugReview();
            if (change == "command") fixture.Change("phase");
            var cyber = fixture.Coordinator.LoadCareerCyberwarePurchase();
            var drug = fixture.Coordinator.LoadCareerCustomDrugRecipe();
            if (change == "forged") { cyber = cyber with { }; drug = drug with { }; }
            else fixture.Change(change);
            int writes = fixture.Writes;
            Reject(() => fixture.Coordinator.UpdateCareerCyberwarePurchaseSelection(cyber, cyber.Selection));
            Reject(() => fixture.Coordinator.ReviewCareerCyberwarePurchase(cyber));
            Reject(() => fixture.Coordinator.ReopenCareerCyberwarePurchase(cyber));
            Reject(() => fixture.Coordinator.UpdateCareerCustomDrugRecipeSelection(drug, drug.Selection));
            Reject(() => fixture.Coordinator.ReviewCareerCustomDrugRecipe(drug));
            Reject(() => fixture.Coordinator.ReopenCareerCustomDrugRecipe(drug));
            await RejectAsync(() => fixture.Coordinator.ConfirmCareerCyberwarePurchaseAsync(cyber));
            await RejectAsync(() => fixture.Coordinator.UndoCareerCyberwarePurchaseAsync(cyber));
            await RejectAsync(() => fixture.Coordinator.ConfirmCareerCustomDrugRecipeAsync(drug));
            await RejectAsync(() => fixture.Coordinator.UndoCareerCustomDrugRecipeAsync(drug));
            Require(fixture.Writes == writes && fixture.WorkspaceWrites == 0 && fixture.RuleMutations == 0,
                change + ": old rendered action reached a write/commit/undo.");
            Require(fixture.Owners.ActiveLeases == 0, "Rejected action leaked owner lease.");
            if (change is "owner" or "owner-aba")
            {
                var freshCyber = fixture.Coordinator.LoadCareerCyberwarePurchase();
                var freshDrug = fixture.Coordinator.LoadCareerCustomDrugRecipe();
                fixture.Coordinator.UpdateCareerCyberwarePurchaseSelection(freshCyber,
                    freshCyber.Selection with { MarkupPercent = 1 });
                fixture.Coordinator.UpdateCareerCustomDrugRecipeSelection(freshDrug,
                    freshDrug.Selection with { Name = "current account" });
                Require(fixture.Writes == writes + 2 && fixture.WorkspaceWrites == 0,
                    "Fresh current-owner pages stopped accepting draft edits.");
            }
            Console.WriteLine("PASS all commerce gestures rejected: " + change);
        }
        using var mutable = new Fixture();
        mutable.PrepareDrugReview();
        var issued = mutable.Coordinator.LoadCareerCustomDrugRecipe();
        ((CharacterCustomDrugComponentSelection[])issued.Selection.Components)[0] = new(mutable.FoundationId, 2);
        Reject(() => mutable.Coordinator.ReviewCareerCustomDrugRecipe(issued));
        Console.WriteLine("PASS issued component-list mutation rejected.");

        static async Task RejectAsync(Func<Task> action)
        {
            try { await action(); }
            catch (InvalidOperationException) { return; }
            throw new InvalidOperationException("Stale async commerce action was accepted.");
        }
    }

    public static Task RunAsync()
    {
        foreach (string selector in new[] { "cyberware", "cyberware-grade", "drug-grade", "drug-component" })
        {
            using var fixture = new Fixture();
            ContentPage page = selector switch
            {
                "cyberware" => new Sr5CareerCyberwareCatalogPage(fixture.Coordinator),
                "cyberware-grade" => new Sr5CareerCyberwareGradePage(fixture.Coordinator),
                "drug-grade" => new Sr5CareerCustomDrugGradePage(fixture.Coordinator),
                _ => new Sr5CareerCustomDrugComponentPage(fixture.Coordinator)
            };
            var navigation = CommerceSelectionNavigation.Create();
            ((NavigationProxy)page.Navigation).Inner = (INavigation)navigation;
            object snapshot = selector.StartsWith("cyberware", StringComparison.Ordinal)
                ? fixture.Coordinator.LoadCareerCyberwarePurchase()
                : fixture.Coordinator.LoadCareerCustomDrugRecipe();
            page.GetType().GetMethod("RenderCatalog", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, [snapshot]);
            var body = (VerticalStackLayout)((ScrollView)page.Content!).Content;
            Button[] choices = body.Children.OfType<Border>()
                .Select(card => card.Content).OfType<Grid>()
                .SelectMany(row => row.Children.OfType<Button>()).ToArray();
            Require(choices.Length >= 2, selector + ": missing distinct real choices");

            ((IButtonController)choices[0]).SendClicked();
            Require(navigation.PopCalls == 1 && fixture.Writes == 1, selector + ": first choice not saved/popped");
            object? accepted = fixture.CyberCheckpoint ?? (object?)fixture.DrugCheckpoint;
            string acceptedId = selector switch
            {
                "cyberware" => $"career-cyberware-source-{fixture.CyberCheckpoint!.Selection.SourceId.Value:N}",
                "cyberware-grade" => $"career-cyberware-grade-{fixture.CyberCheckpoint!.Selection.GradeId.Value:N}",
                "drug-grade" => $"career-custom-drug-grade-{fixture.DrugCheckpoint!.Selection.GradeId.Value:N}",
                _ => $"career-custom-drug-component-{fixture.DrugCheckpoint!.Selection.Components.Single().ComponentId.Value:N}-{fixture.DrugCheckpoint.Selection.Components.Single().Level}"
            };
            Require(choices[0].AutomationId == acceptedId, selector + ": wrong exact source/grade/effect saved");
            ((IButtonController)choices[1]).SendClicked();
            ((IButtonController)choices[0]).SendClicked();
            Require(navigation.PopCalls == 1 && fixture.Writes == 1, selector + ": overlap replayed save or pop");
            Require(Equals(accepted, fixture.CyberCheckpoint ?? (object?)fixture.DrugCheckpoint),
                selector + ": rejected tap changed the saved draft");
            navigation.Complete(page);
            Require(fixture.WorkspaceWrites == 0, selector + ": selecting a draft must not commit a character");
            Console.WriteLine("PASS exclusive native selector: " + selector);
        }

        foreach (string change in new[] { "workspace", "revision", "character", "catalog", "rules", "draft", "phase" })
        {
            using var fixture = new Fixture();
            if (change == "phase") fixture.PrepareDrugReview();
            var cyber = fixture.Coordinator.LoadCareerCyberwarePurchase();
            var drug = fixture.Coordinator.LoadCareerCustomDrugRecipe();
            fixture.Change(change);
            int writes = fixture.Writes;
            if (change != "rules") // Cyberware has no separate rules digest.
                Reject(() => fixture.Coordinator.UpdateCareerCyberwarePurchaseSelection(
                    cyber, cyber.Selection with { MarkupPercent = 37m }));
            Reject(() => fixture.Coordinator.UpdateCareerCustomDrugRecipeSelection(
                drug, drug.Selection with { Name = "stale-tap" }));
            Require(fixture.CyberCheckpoint?.Selection.MarkupPercent != 37m
                && fixture.DrugCheckpoint?.Selection.Name != "stale-tap"
                && fixture.WorkspaceWrites == 0, change + ": stale selection escaped");
            if (change is "workspace" or "draft" or "phase")
                Require(fixture.Writes == writes, change + ": stale tap wrote a draft");
            Console.WriteLine("PASS stale selector rejection: " + change);
        }

        using (var fixture = new Fixture())
        {
            // Preferences restoration allocates new component lists. Equal
            // values must still be selectable after a service/process restart.
            var initial = fixture.Coordinator.LoadCareerCustomDrugRecipe();
            var selected = fixture.Coordinator.UpdateCareerCustomDrugRecipeSelection(initial,
                initial.Selection with { Components = [new(fixture.FoundationId, 1)] });
            var restarted = new Sr5CareerCustomDrugRecipeService(fixture, fixture, fixture);
            var updated = restarted.UpdateSelection(selected, selected.Selection with { Name = "restored" });
            Require(updated.Selection.Name == "restored"
                && updated.Selection.Components.SequenceEqual(selected.Selection.Components)
                && fixture.WorkspaceWrites == 0, "equal restored selection was rejected or committed");
            Console.WriteLine("PASS value-equal restored component selection");
        }
        Console.WriteLine("Commerce selector regressions passed: 12 scenarios.");
        return Task.CompletedTask;
    }

    public static async Task RunBackgroundAsync()
    {
        using var ui = new AfterRunAuthorityHarness.IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            foreach (bool drug in new[] { false, true })
            foreach (string change in new[] { "none", "workspace", "revision", "character", "owner-aba", "page", "cancel", "throw", "draft", "preparation-revision" })
            {
                using var fixture = new Fixture(ownerBound: true);
                using var release = new ManualResetEventSlim();
                using var cancel = new CancellationTokenSource();
                var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                bool currentPage = true;
                fixture.BeforePrepare = () =>
                {
                    Require(!ReferenceEquals(SynchronizationContext.Current, ui), "Core preparation ran on UI thread.");
                    Require(fixture.Owners.ActiveLeases == 0, "Owner lease crossed background preparation.");
                    entered.TrySetResult();
                    Require(release.Wait(TimeSpan.FromSeconds(10)), "Test did not release Core preparation.");
                    if (change == "throw") throw new InvalidOperationException("injected preparation failure");
                };
                MethodInfo load = typeof(RunnerSessionCoordinator).GetMethod(drug
                    ? "LoadCareerCustomDrugRecipeAsync" : "LoadCareerCyberwarePurchaseAsync",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                Task pending = (Task)load.Invoke(fixture.Coordinator,
                    [cancel.Token, (Func<bool>)(() => currentPage)])!;
                try
                {
                    await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    Require(!pending.IsCompleted, "Load completed before preparation was released.");
                    // This continuation itself is a UI heartbeat while Core is
                    // blocked. Account changes must not wait on a worker lease.
                    Require(ReferenceEquals(SynchronizationContext.Current, ui), "Test lost UI context.");
                    if (change == "page") currentPage = false;
                    else if (change == "cancel") cancel.Cancel();
                    else if (change is not ("none" or "throw"))
                    {
                        fixture.BeforePrepare = null;
                        fixture.Change(change);
                    }
                }
                finally { release.Set(); }
                int writes = fixture.Writes;
                try
                {
                    await pending;
                    Require(change is not ("cancel" or "throw"), "Preparation failure/cancellation was swallowed.");
                    object result = pending.GetType().GetProperty("Result")!.GetValue(pending)!;
                    bool ready = (bool)result.GetType().GetProperty("IsReady")!.GetValue(result)!;
                    Require(ready == (change is "none" or "draft"), "Stale background result admitted: " + change);
                    if (change == "draft")
                        Require(drug
                            ? ((Sr5CareerCustomDrugRecipeSnapshot)result).Selection.Name == "newer"
                            : ((Sr5CareerCyberwarePurchaseSnapshot)result).Selection.FreeCost,
                            "Completion replaced the newer saved draft.");
                }
                catch (OperationCanceledException) when (change == "cancel") { }
                catch (InvalidOperationException ex) when (change == "throw" && ex.Message == "injected preparation failure") { }
                Require(fixture.Writes == writes && fixture.WorkspaceWrites == 0,
                    "Background load changed a draft or runner after stale/canceled preparation.");
                Require(fixture.Owners.ActiveLeases == 0, "Load leaked an owner lease.");
                Console.WriteLine("PASS background commerce: " + (drug ? "drug/" : "cyberware/") + change);
            }

            foreach (string kind in new[] { "purchase", "cyberware", "cyberware-grade", "recipe", "drug-grade", "drug-component" })
            {
                using var fixture = new Fixture(ownerBound: true);
                // These tests exercise the actual page lifecycle, not account
                // startup. The injected presenters already contain the runner.
                typeof(RunnerSessionCoordinator).GetField("_initialized", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(fixture.Coordinator, true);
                ContentPage page = kind switch
                {
                    "purchase" => new Sr5CareerCyberwarePurchasePage(fixture.Coordinator),
                    "cyberware" => new Sr5CareerCyberwareCatalogPage(fixture.Coordinator),
                    "cyberware-grade" => new Sr5CareerCyberwareGradePage(fixture.Coordinator),
                    "recipe" => new Sr5CareerCustomDrugRecipePage(fixture.Coordinator),
                    "drug-grade" => new Sr5CareerCustomDrugGradePage(fixture.Coordinator),
                    _ => new Sr5CareerCustomDrugComponentPage(fixture.Coordinator)
                };
                var body = (VerticalStackLayout)((ScrollView)page.Content!).Content!;
                using var release = new ManualResetEventSlim();
                var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var prepared = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                fixture.BeforePrepare = () =>
                {
                    entered.TrySetResult();
                    Require(release.Wait(TimeSpan.FromSeconds(10)), "Page preparation was not released.");
                    prepared.TrySetResult();
                };
                void Lifecycle(string name) => page.GetType().GetMethod(name,
                    BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null)!.Invoke(page, null);
                int offUi = 0;
                body.ChildAdded += (_, _) => { if (!ReferenceEquals(SynchronizationContext.Current, ui)) offUi++; };
                Task firstAppearance = ui.BeginAsyncVoid(() => Lifecycle("OnAppearing"));
                try
                {
                    await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    Require(!body.IsEnabled && body.Children.OfType<ActivityIndicator>().Any(),
                        kind + ": loading did not keep old controls inert.");
                    Lifecycle("OnDisappearing");
                }
                finally { release.Set(); }
                await prepared.Task.WaitAsync(TimeSpan.FromSeconds(10));
                await firstAppearance.WaitAsync(TimeSpan.FromSeconds(10));
                fixture.BeforePrepare = null;
                var rendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                body.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(body.IsEnabled) && body.IsEnabled) rendered.TrySetResult();
                };
                Task secondAppearance = ui.BeginAsyncVoid(() => Lifecycle("OnAppearing"));
                try
                {
                    await rendered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    Require(!body.Children.OfType<ActivityIndicator>().Any() && body.Children.Count > 1,
                        kind + ": reopened page retained loading state.");
                    Require(offUi == 0, kind + ": visual tree touched outside UI context.");
                    Require(fixture.Writes == 0 && fixture.WorkspaceWrites == 0,
                        kind + ": page appearance mutated a draft or runner.");
                    Console.WriteLine("PASS real commerce page disappear/reappear: " + kind);
                }
                finally { Lifecycle("OnDisappearing"); }
                await secondAppearance.WaitAsync(TimeSpan.FromSeconds(10));
            }
        });
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Stale selector was accepted.");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class CommerceTestDirectory : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("chummer-commerce-owners-");
        public string Path => _directory.FullName;
        public void Dispose() => _directory.Delete(recursive: true);
    }

    private sealed class Fixture : IDisposable, ICharacterCyberwarePurchaseAuthority,
        ICharacterCustomDrugAuthority, ISr5CareerCyberwareWorkspaceStore, ISr5CareerCustomDrugWorkspaceStore,
        ISr5CareerCyberwarePurchaseCheckpointStore, ISr5CareerCustomDrugRecipeCheckpointStore
    {
        private static readonly CharacterWorkspaceId Workspace = new("commerce-selector-runner");
        private string _xml = "<character><created>True</created><nuyen>100000</nuyen><cyberwares/><expenses/></character>";
        private long _revision = 7;
        private string _catalog = new('a', 64);
        private string _rules = new('c', 64);
        private CharacterOverviewState _state;
        public RunnerSessionCoordinator Coordinator { get; }
        public int Writes { get; private set; }
        public int WorkspaceWrites { get; private set; }
        public int RuleMutations { get; private set; }
        public AfterRunAuthorityHarness.ControlledLinkedOwner Owners { get; } = new();
        public Action? BeforePrepare { get; set; }
        private bool _wrongPreparationRevision;
        public Sr5CareerCyberwarePurchaseCheckpoint? CyberCheckpoint { get; private set; }
        public Sr5CareerCustomDrugRecipeCheckpoint? DrugCheckpoint { get; private set; }
        private readonly CharacterCyberwareSourceId _source = new(Guid.Parse("11111111-1111-4111-8111-111111111111"));
        private readonly CharacterCyberwareGradeId _grade = new(Guid.Parse("22222222-2222-4222-8222-222222222222"));
        private readonly CharacterCustomDrugGradeId _drugGrade = new(Guid.Parse("33333333-3333-4333-8333-333333333333"));
        public CharacterCustomDrugComponentId FoundationId { get; } = new(Guid.Parse("44444444-4444-4444-8444-444444444444"));

        public Fixture(bool ownerBound = true)
        {
            var creation = Program.NewCreationOverview(Workspace, _revision, _revision);
            _state = creation with
            {
                Profile = creation.Profile! with { Created = true },
                Rules = new CharacterRulesSection("SR5", "", "", 0, 0, 0, 0, [])
            };
            if (ownerBound)
                _state = _state with
                {
                    DisplayOwnerContext = Owners.Capture(),
                    Session = _state.Session with { OwnerContext = Owners.Capture() }
                };
            Coordinator = new RunnerSessionCoordinator(
                StrictPageProxy.Create<ICharacterOverviewPresenter>(() => _state),
                ownerBound ? CommerceOwnerClient.Create(Owners) : null!, null!, null!, null!, null!, null!,
                StrictPageProxy.Create<IShellPresenter>(() => ShellState.Empty with { OwnerContext = _state.DisplayOwnerContext }),
                null!, null!, null!, null!, null!,
                StrictPageProxy.Create<Chummer.Android.Platform.IAndroidAccountLinkService>(), null!, null!,
                careerCyberwarePurchaseService: new(this, this, this),
                careerCustomDrugRecipeService: new(this, this, this),
                damageJournalOwnerAccessor: ownerBound ? Owners : null,
                commerceOwnerAdmission: ownerBound ? new CareerCommerceOwnerAdmission(Owners) : null);
        }

        public void Dispose() => Coordinator.Dispose();

        public void Change(string change)
        {
            switch (change)
            {
                case "preparation-revision": _wrongPreparationRevision = true; break;
                case "owner":
                    Owners.Set(new OwnerScope("another-owner"));
                    _state = _state with { DisplayOwnerContext = Owners.Capture(),
                        Session = _state.Session with { OwnerContext = Owners.Capture() } };
                    break;
                case "owner-aba":
                    Owners.Set(new OwnerScope("another-owner"));
                    Owners.Set(OwnerScope.LocalSingleUser);
                    _state = _state with { DisplayOwnerContext = Owners.Capture(),
                        Session = _state.Session with { OwnerContext = Owners.Capture() } };
                    break;
                case "command":
                    CyberCheckpoint = CyberCheckpoint! with { Command = CyberCheckpoint!.Command! with
                        { NewInstanceId = new(Guid.NewGuid()) } };
                    DrugCheckpoint = DrugCheckpoint! with { Command = DrugCheckpoint!.Command! with
                        { NewDrugInstanceId = new(Guid.NewGuid()) } };
                    break;
                case "workspace": _state = _state with { WorkspaceId = new("another-runner") }; break;
                case "revision": _revision++; break;
                case "character": _xml = _xml.Replace("100000", "99999", StringComparison.Ordinal); break;
                case "catalog": _catalog = new('b', 64); break;
                case "rules": _rules = new('d', 64); break;
                case "draft":
                    var cyber = Coordinator.LoadCareerCyberwarePurchase();
                    Coordinator.UpdateCareerCyberwarePurchaseSelection(cyber, cyber.Selection with { FreeCost = true });
                    var drug = Coordinator.LoadCareerCustomDrugRecipe();
                    Coordinator.UpdateCareerCustomDrugRecipeSelection(drug, drug.Selection with { Name = "newer" });
                    break;
                case "phase":
                    Coordinator.ReviewCareerCustomDrugRecipe(Coordinator.LoadCareerCustomDrugRecipe());
                    Coordinator.ReviewCareerCyberwarePurchase(Coordinator.LoadCareerCyberwarePurchase());
                    break;
                default: throw new InvalidOperationException(change);
            }
        }

        public void PrepareDrugReview()
        {
            var original = Coordinator.LoadCareerCustomDrugRecipe();
            Coordinator.UpdateCareerCustomDrugRecipeSelection(original, original.Selection with
                { Name = "ready", Components = [new(FoundationId, 1)] });
        }

        public CharacterCyberwarePurchasePreparation Prepare(string xml, long revision)
        {
            BeforePrepare?.Invoke();
            if (_wrongPreparationRevision) revision++;
            CharacterCyberwarePurchaseGrade[] grades =
            [
                new(_grade, "Standard", 1m, 1m, 0),
                new(new(Guid.Parse("55555555-5555-4555-8555-555555555555")), "Alpha", 1.2m, 0.8m, 2)
            ];
            var entry = new CharacterCyberwarePurchaseCatalogEntry(_source, "Simrig", "Headware",
                "0.1", "[0]", "6", "5000", "SR5", "452", true, "", [], grades);
            return new(true, [], revision, CharacterCyberwarePurchaseRules.ComputeCharacterDigest(xml),
                _catalog, "sr5-test", new('b', 64), 100000m, false, null, null,
                new(false, false, 1m, false, 1m, 2, false, []),
                [entry, entry with { SourceId = new(Guid.Parse("66666666-6666-4666-8666-666666666666")), Name = "Other" }], []);
        }

        public CharacterCyberwarePurchaseQuote Quote(CharacterCyberwarePurchasePreparation preparation, CharacterCyberwarePurchaseSelection selection)
            => CharacterCyberwarePurchaseRules.Quote(preparation, selection);
        public CharacterCyberwarePurchaseCommitResult Commit(string xml, long revision, CharacterCyberwarePurchaseCommand command)
        { RuleMutations++; throw new InvalidOperationException("Selector must not commit."); }
        public CharacterCyberwarePurchaseCommitResult Undo(string xml, long revision, CharacterCyberwarePurchaseUndoCommand command)
        { RuleMutations++; throw new InvalidOperationException("Selector must not undo."); }

        public CharacterCustomDrugPreparation Prepare(string xml, long revision, CharacterCustomDrugContext context)
        {
            BeforePrepare?.Invoke();
            if (_wrongPreparationRevision) revision++;
            var foundation = new CharacterCustomDrugComponentSource(FoundationId, "Foundation",
                CharacterCustomDrugComponentCategory.Foundation, 1, 2, CharacterCustomDrugLegality.Restricted,
                100m, 1, 1, "SR5", "414", new('f', 64), ["source-anchor"],
                [new(1, [], [], [], [], 0, 0, 0, 0, 1), new(2, [], [], [], [], 0, 0, 0, 0, 1)]);
            var grade = new CharacterCustomDrugGrade(_drugGrade, "Standard", 1m, 0, "SR5", new('d', 64), ["grade"]);
            return new(true, [], context, CharacterCustomDrugQuotePurpose.RecipeDefinition, revision,
                CharacterCustomDrugRules.ComputeCharacterDigest(xml), _catalog, _rules, "sr5-test", 100000m,
                new(true, false, false, 8, 100m, 2),
                [grade, grade with { Id = new(Guid.Parse("77777777-7777-4777-8777-777777777777")), Name = "Other" }],
                [foundation]);
        }
        public CharacterCustomDrugQuote Quote(CharacterCustomDrugPreparation preparation, CharacterCustomDrugSelection selection)
            => CharacterCustomDrugRules.Quote(preparation, selection);
        public CharacterCustomDrugCommitResult Commit(string xml, long revision, CharacterCustomDrugContext context, CharacterCustomDrugCommitCommand command)
        { RuleMutations++; throw new InvalidOperationException("Selector must not commit."); }
        public CharacterCustomDrugCommitResult LookupReceipt(string xml, long revision, CharacterCustomDrugContext context, CharacterCustomDrugCommitCommand command)
            => throw new InvalidOperationException("Unexpected receipt lookup.");
        public CharacterCustomDrugCommitResult Undo(string xml, long revision, CharacterCustomDrugContext context, CharacterCustomDrugUndoCommand command)
        { RuleMutations++; throw new InvalidOperationException("Selector must not undo."); }

        Sr5CareerCyberwareWorkspaceSnapshot? ISr5CareerCyberwareWorkspaceStore.Read(CharacterWorkspaceId id)
            => new(id, _revision, _revision, new(_xml, "sr5"));
        Sr5CareerCustomDrugWorkspaceSnapshot? ISr5CareerCustomDrugWorkspaceStore.Read(CharacterWorkspaceId id)
            => new(id, _revision, _revision, new(_xml, "sr5"));
        public Sr5CareerCyberwareWorkspaceWriteResult ReplaceAndCheckpoint(Sr5CareerCyberwareWorkspaceSnapshot expected, string xml)
        { WorkspaceWrites++; throw new InvalidOperationException("Unexpected workspace write."); }
        public Sr5CareerCustomDrugWorkspaceWriteResult ReplaceAndCheckpoint(Sr5CareerCustomDrugWorkspaceSnapshot expected, string xml)
        { WorkspaceWrites++; throw new InvalidOperationException("Unexpected workspace write."); }

        Sr5CareerCyberwarePurchaseCheckpoint? ISr5CareerCyberwarePurchaseCheckpointStore.Read(CharacterWorkspaceId id)
            => CyberCheckpoint;
        Sr5CareerCustomDrugRecipeCheckpoint? ISr5CareerCustomDrugRecipeCheckpointStore.Read(CharacterWorkspaceId id)
            => DrugCheckpoint is { } checkpoint
                ? checkpoint with { Selection = checkpoint.Selection with { Components = checkpoint.Selection.Components.ToArray() } }
                : null;
        public void Write(Sr5CareerCyberwarePurchaseCheckpoint checkpoint) { CyberCheckpoint = checkpoint; Writes++; }
        public void Write(Sr5CareerCustomDrugRecipeCheckpoint checkpoint) { DrugCheckpoint = checkpoint; Writes++; }
        void ISr5CareerCyberwarePurchaseCheckpointStore.Clear(CharacterWorkspaceId id) => CyberCheckpoint = null;
        void ISr5CareerCustomDrugRecipeCheckpointStore.Clear(CharacterWorkspaceId id) => DrugCheckpoint = null;
    }
}

public interface ICommerceOwnerClient : IChummerClient, IOwnerBoundShellStateClient { }

public class CommerceOwnerClient : DispatchProxy
{
    private IOwnerContextLeaseAccessor _owners = null!;
    public static ICommerceOwnerClient Create(IOwnerContextLeaseAccessor owners)
    {
        var client = Create<ICommerceOwnerClient, CommerceOwnerClient>();
        ((CommerceOwnerClient)(object)client)._owners = owners;
        return client;
    }
    protected override object? Invoke(MethodInfo? method, object?[]? args)
        => method?.Name == "CaptureOwnerContext" ? _owners.Capture()
            : throw new InvalidOperationException("Unexpected commerce client call: " + method?.Name);
}

public class CommerceSelectionNavigation : DispatchProxy
{
    private readonly TaskCompletionSource<Page> _pop = new();
    public int PopCalls { get; private set; }
    public static CommerceSelectionNavigation Create()
    {
        INavigation proxy = Create<INavigation, CommerceSelectionNavigation>();
        return (CommerceSelectionNavigation)proxy;
    }
    public void Complete(Page page) => _pop.SetResult(page);
    protected override object? Invoke(MethodInfo? method, object?[]? args)
        => method?.Name switch
        {
            "PopAsync" => Pop(),
            "get_ModalStack" or "get_NavigationStack" => Array.Empty<Page>(),
            _ => throw new InvalidOperationException("Unexpected navigation: " + method?.Name)
        };
    private Task<Page> Pop() { PopCalls++; return _pop.Task; }
}
