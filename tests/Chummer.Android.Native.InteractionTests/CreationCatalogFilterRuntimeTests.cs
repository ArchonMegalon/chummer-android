using System.Globalization;
using Chummer.Android.Native;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Workspaces;
using Chummer.Presentation.Overview;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    internal static async Task RunCatalogFiltersAsync(string contentRoot)
    {
        QualityFilterBoundaries();
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            var culture = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-AT");
                var owners = new ControlledLinkedOwner();
                await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners,
                    creationFinalization: true, creationAttributes: true, creationSkills: true,
                    productionCreationOverview: true);
                var before = PrepareActualFinalizationReadyContext(runtime, stopBeforeGear: true);
                await HydrateFinalizationOwnerAsync(runtime, owners, before);
                await runtime.Coordinator.InitializeAsync();
                await HydrateFinalizationOwnerAsync(runtime, owners, before);
                var gear = new CatalogFilterGearProbe(new CharacterCreationGearInteractionPresenter(
                    runtime.Services.GetRequiredService<ICharacterCreationGearService>(),
                    runtime.Services.GetRequiredService<IOwnerBoundCharacterCreationGearService>()));
                var page = new CreationGearPage(runtime.Coordinator, gear, runtime.Presenter);
                var navigation = new NavigationPage(page);
                var window = new Window(navigation);
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing")));
                T Control<T>(string id) where T : Element
                    => MinimalVisible(page).OfType<T>().Single(x => x.AutomationId == id);
                var type = Control<Picker>("creation-gear-type");
                var search = Control<SearchBar>("creation-gear-search");
                Require(type.Items.Count > 2, "Actual Core catalog has no selectable equipment types.");
                Button[] Rows() => MinimalVisible(page).OfType<Button>()
                    .Where(x => x.AutomationId?.StartsWith("creation-gear-catalog-") == true
                        && x.AutomationId is not "creation-gear-catalog-previous" and not "creation-gear-catalog-next").ToArray();
                var originalRows = Rows();
                Require(originalRows.Length > 0, "Actual admitted catalog is empty.");
                var add = originalRows[0];
                ((IButtonController)add).SendClicked();
                var remove = MinimalVisible(page).OfType<Button>()
                    .Single(x => x.AutomationId?.StartsWith("creation-gear-basket-") == true
                        && x.AutomationId.EndsWith("-remove"));
                int loads = gear.LoadCalls;
                // The fixed-price admitted fixture intentionally omits some
                // familiar variable-price categories; use an actually offered type.
                int categoryIndex = 2;
                string selectedType = type.Items[categoryIndex];
                type.SelectedIndex = categoryIndex;
                Require(Rows().SequenceEqual(originalRows),
                    "Type selection rebuilt native rows before the Picker callback returned.");
                await page.CatalogRefresh;
                var filtered = Rows();
                Require(filtered.Length > 0 && filtered.All(row => MinimalVisible((Grid)row.Parent)
                    .OfType<Label>().Any(x => x.Text?.Contains(selectedType) == true)), "Wrong type escaped the filter.");
                search.Text = "no-such-equipment-1909";
                ((ISearchBarController)search).OnSearchButtonPressed();
                Require(Rows().Length == 0, "Search did not combine with type.");
                Require(ReferenceEquals(search, Control<SearchBar>("creation-gear-search"))
                    && ReferenceEquals(type, Control<Picker>("creation-gear-type"))
                    && MinimalVisible(page).Contains(remove), "Filtering detached controls or the selected basket.");
                search.Text = "";
                Require(type.SelectedIndex == categoryIndex, "Clearing search reset the type.");
                type.SelectedIndex = 0;
                var obsoleteRefresh = page.CatalogRefresh;
                type.SelectedIndex = categoryIndex;
                await obsoleteRefresh;
                await page.CatalogRefresh;
                Require(Rows().All(row => MinimalVisible((Grid)row.Parent)
                    .OfType<Label>().Any(x => x.Text?.Contains(selectedType) == true)),
                    "A superseded Picker callback replaced the latest category.");
                Require(gear.LoadCalls == loads && gear.ConfirmCalls == 0, "Browsing reread or mutated Core.");
                ((IButtonController)remove).SendClicked();
                Require(!MinimalVisible(page).OfType<Button>().Any(x => x.AutomationId == remove.AutomationId),
                    "An item hidden by type could not be removed from the basket.");
                var saved = new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!;
                Require(saved.ContentRevision == before.ContentRevision && saved.SavedRevision == before.SavedRevision,
                    "Filters changed the durable runner.");
                type.SelectedIndex = 0;
                var leavingRows = Rows();
                IssuedPageLifecycle(page, "OnDisappearing");
                await page.CatalogRefresh;
                Require(Rows().SequenceEqual(leavingRows), "Queued category rendering survived departure.");
                type.SelectedIndex = categoryIndex;
                await page.CatalogRefresh;
                Require(gear.LoadCalls == loads && gear.ConfirmCalls == 0, "Departed type control dispatched Core.");
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing")));
                Require(Control<Picker>("creation-gear-type").Items.Count > 2, "Re-entry lost equipment types.");
                IssuedPageLifecycle(page, "OnDisappearing");
                Console.WriteLine("PASS actual Core/MAUI equipment type+search, basket preservation/removal, no reread/write, re-entry");

                var qualities = new CreationQualitiesPage(runtime.Coordinator,
                    new CharacterCreationQualitiesCheckpointStore(new QualitiesFeedbackBackend()));
                await navigation.PushAsync(qualities, animated: false);
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(qualities, "OnAppearing")));
                T QualityControl<T>(string id) where T : Element
                    => MinimalVisible(qualities).OfType<T>().Single(x => x.AutomationId == id);
                var polarity = QualityControl<Picker>("creation-qualities-polarity");
                var cost = QualityControl<Picker>("creation-qualities-cost");
                var qualitySearch = QualityControl<SearchBar>("creation-qualities-search");
                polarity.SelectedIndex = 1;
                cost.SelectedIndex = 2;
                Require(ReferenceEquals(qualitySearch, QualityControl<SearchBar>("creation-qualities-search")),
                    "Quality filters detached the search field.");
                var sort = QualityControl<Picker>("creation-qualities-sort");
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => sort.SelectedIndex = 3));
                Require(QualityControl<Picker>("creation-qualities-polarity").SelectedIndex == 1
                    && QualityControl<Picker>("creation-qualities-cost").SelectedIndex == 2
                    && QualityControl<Picker>("creation-qualities-sort").SelectedIndex == 3,
                    "Relevance sort discarded the other filters.");
                Require(new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!.ContentRevision
                    == before.ContentRevision, "Recommendations mutated the runner.");
                IssuedPageLifecycle(qualities, "OnDisappearing");
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(qualities, "OnAppearing")));
                Require(QualityControl<Picker>("creation-qualities-sort").SelectedIndex == 3,
                    "Re-entry discarded relevance sorting.");
                IssuedPageLifecycle(qualities, "OnDisappearing");
                Console.WriteLine("PASS actual MAUI quality combined filters, async recommendations, readonly re-entry");
                GC.KeepAlive(window);
            }
            finally { CultureInfo.CurrentUICulture = culture; }
        });
    }

    private static void QualityFilterBoundaries()
    {
        CharacterCreationQualitiesDesktopOption Option(string id, CharacterCreationQualityType type, int cost)
            => new(id, Guid.NewGuid(), id, id, type, 1, cost, false, false, true, null, null, null, []);
        var positive = Option("positive", CharacterCreationQualityType.Positive, 5);
        var negative = Option("negative", CharacterCreationQualityType.Negative, -10);
        var expensive = positive with { OptionId = "expensive", KarmaCost = 11 };
        var rows = new[] { positive, negative, expensive };
        IReadOnlyDictionary<string, QualityFitHint> hints = new Dictionary<string, QualityFitHint>
            { [expensive.OptionId] = new(4, "Sneaking") };
        Require(new CreationQualityFilters(1, 2).Apply(rows, _ => true, _ => false, hints)
            .SequenceEqual([positive]), "Positive and Karma filters are not conjunctive.");
        Require(new CreationQualityFilters(2, 3).Apply(rows, _ => true, _ => false, hints)
            .SequenceEqual([negative]), "Negative Karma magnitude filter failed.");
        Require(new CreationQualityFilters(1, 2).Apply(rows, _ => false, id => id == negative.OptionId, hints)
            .SequenceEqual([negative]), "Selected quality became inaccessible for removal.");
        Require(new CreationQualityFilters(Sort: 3).Apply(rows, _ => true, _ => false, hints)[0] == expensive,
            "Character-fit sort ignored the current skill projection.");
        Require(new CreationQualityFilters(Sort: 1).Apply(rows, _ => true, _ => false, hints)
            .SequenceEqual([positive, negative, expensive]), "Karma sort used signed negative values.");
        Require(CreationQualityFilters.Cost(negative with { KarmaCost = int.MinValue }) == 2147483648L,
            "Cost magnitude overflowed.");
        CharacterCreationQualityCatalogOption effect = new("test", Guid.NewGuid(), "test", "Test",
            CharacterCreationQualityType.Positive, 1, 5, 1, false, true, true, false, true, true,
            null, null, null, [], "<quality><bonus><specificskill><name>Sneaking</name><bonus>2</bonus></specificskill></bonus></quality>", "", "");
        CharacterCreationSkillProjection skill = new("test", CharacterCreationSkillKinds.Active, "Sneaking",
            "Physical Active", "AGI", null, 3, 3, 3, null, null, false, true, [], []);
        Require(CreationQualityFit.Project([effect], [skill], default).Count == 1, "Direct skill bonus not recommended.");
        Require(CreationQualityFit.Project([effect], [skill with { EffectiveRating = 0 }], default).Count == 0,
            "Untrained skill was inferred as an archetype.");
        Require(CreationQualityFit.Project([effect with { Type = CharacterCreationQualityType.Negative }], [skill], default).Count == 0,
            "Disadvantage presented as free benefit.");
        Require(CreationQualityFit.Project([effect with { SourceNodeXml = "<broken" }], [skill], default).Count == 0,
            "Unknown effect received an invented recommendation.");
        Require(CreationQualityFit.Project([effect with { SourceNodeXml = "<!DOCTYPE quality [<!ENTITY x SYSTEM 'file:///invalid'>]><quality>&x;</quality>" }], [skill], default).Count == 0,
            "Recommendation accepted external XML entities.");
        foreach (string locale in new[] { "en-GB", "de-AT", "es-MX" })
            foreach (string key in new[] { "Qualities.Filter.Fit", "Qualities.Filter.AnyCost", "Gear.Filter.Type", "Gear.Filter.All" })
                Require(CreationFlowStrings.Get(key, "missing", CultureInfo.GetCultureInfo(locale)) != "missing",
                    "Missing filter label: " + locale + " " + key);
        Console.WriteLine("PASS quality polarity/cost boundaries, retained selection, deterministic fit, unknown/negative safety, locales");
    }

    private sealed class CatalogFilterGearProbe(ICharacterCreationGearInteractionPresenter inner)
        : ICharacterCreationGearInteractionPresenter
    {
        public int LoadCalls, ConfirmCalls;
        public CharacterCreationGearInteractionLoadResult Load(CharacterOverviewState display)
        { LoadCalls++; return inner.Load(display); }
        public CharacterCreationGearInteractionPrepareResult Prepare(CharacterOverviewState display, IReadOnlyList<CharacterCreationGearSelection> basket)
            => inner.Prepare(display, basket);
        public CharacterCreationGearInteractionConfirmResult Confirm(CharacterOverviewState display, CharacterCreationGearConfirmation confirmation)
        { ConfirmCalls++; return inner.Confirm(display, confirmation); }
        public CharacterCreationGearInteractionReceiptLookupResult LookupReceipt(CharacterOverviewState display, string key)
            => inner.LookupReceipt(display, key);
    }
}
