using System.Globalization;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

/// <summary>
/// Phone renderer for the typed creation Gear commerce boundary. Catalog facts,
/// prices, legality, constraints, previews, and persistence remain Core-owned.
/// </summary>
public sealed class CreationGearPage : NativePageBase
{
    // Keep native view creation bounded on small phones; the full admitted
    // catalogue remains searchable and reachable through the existing pager.
    private const int CatalogPageSize = 12;
    private long _catalogRefreshGeneration;
    internal Task CatalogRefresh { get; private set; } = Task.CompletedTask;
    private readonly ICharacterCreationGearInteractionPresenter _gear;
    private readonly ICharacterOverviewPresenter _overview;
    private readonly AndroidSurfaceCopy _copy;
    private VerticalStackLayout _technicalDetails = new() { Spacing = 6 };
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };
    private Dictionary<string, int> _basket = new(StringComparer.Ordinal);
    private string? _initializedSnapshotDigest;
    private string _filter = string.Empty;
    private string? _category;
    private readonly VerticalStackLayout _catalog = new() { Spacing = 14 };
    private readonly VerticalStackLayout _catalogDetails = new() { Spacing = 6 };
    private int _catalogOffset;
    private CharacterOverviewState? _loadedDisplay;
    private CharacterCreationGearInteractionLoadResult? _loaded;
    private CharacterOverviewState? _initialDisplay;
    private CharacterCreationGearInteractionLoadResult? _initialLoad;
    private readonly CharacterCreationResourcesReceipt? _resourcesReceipt;
    private bool _ready;
    private bool _loading = true;
    private VerticalStackLayout _basketBody = new() { Spacing = 14 };
    private VerticalStackLayout _basketDetails = new() { Spacing = 6 };
    private readonly List<(CharacterCreationGearCatalogOption Option, Border Row)> _catalogRows = [];
    private CancellationTokenSource? _continueLifetime;

    protected override void OnDisappearing()
    {
        _catalogRefreshGeneration++;
        _continueLifetime?.Cancel();
        base.OnDisappearing();
    }

    internal bool IsPreparedDisplayCurrent => _initialDisplay is { } display
        && Coordinator.IsCreationCatalogDisplayCurrent(display);

    public CreationGearPage(
        RunnerSessionCoordinator coordinator,
        ICharacterCreationGearInteractionPresenter gear,
        ICharacterOverviewPresenter overview) : this(coordinator, gear, overview, null, null)
    {
    }

    internal CreationGearPage(
        RunnerSessionCoordinator coordinator,
        ICharacterCreationGearInteractionPresenter gear,
        ICharacterOverviewPresenter overview,
        CharacterOverviewState? initialDisplay,
        CharacterCreationGearInteractionLoadResult? initialLoad,
        CharacterCreationResourcesReceipt? resourcesReceipt = null) : base(coordinator)
    {
        _gear = gear ?? throw new ArgumentNullException(nameof(gear));
        _overview = overview ?? throw new ArgumentNullException(nameof(overview));
        _initialDisplay = initialDisplay;
        _initialLoad = initialLoad;
        _resourcesReceipt = resourcesReceipt;
        _copy = AndroidSurfaceStrings.Resolve();
        Title = _copy["Gear.PageTitle"];
        AutomationId = "creation-gear-page";
        Content = new ScrollView { Content = _body };
        Refresh();
    }

    protected override async Task PrepareForAppearanceRefreshAsync(CancellationToken cancellationToken)
    {
        _loaded = null;
        _loadedDisplay = null;
        _ready = false;
        _loading = true;
        Refresh();
        CharacterOverviewState original = Coordinator.State;
        var admitted = ReferenceEquals(original, _initialDisplay)
            && Coordinator.IsCreationCatalogDisplayCurrent(original) ? _initialLoad : null;
        _initialDisplay = null;
        _initialLoad = null;
        try
        {
            // Only the exact coordinator-admitted first appearance can consume
            // this read. Re-entry always refreshes Core catalogue authority.
            var prepared = admitted is not null
                ? (Load: admitted, Ready: true)
                : await Coordinator.LoadCreationGearForDisplayAsync(_gear, original, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _loadedDisplay = original;
            _loaded = prepared.Load;
            _ready = prepared.Ready;
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
                _loading = false;
        }
    }

    protected override void Refresh()
    {
        if (_continueLifetime is not null && _loadedDisplay is { } continuing
            && Coordinator.IsCreationCatalogDisplayCurrent(continuing)) return;
        _catalogRefreshGeneration++;
        _catalogRows.Clear();
        _basketBody = new() { Spacing = 14 };
        _basketDetails = new() { Spacing = 6 };
        _body.Clear();
        _technicalDetails.Remove(_catalogDetails);
        _technicalDetails = new() { Spacing = 6 };
        _body.Add(NativeTheme.Title(_copy["Gear.Title"]));
        _technicalDetails.Add(NativeTheme.Body(_copy["Gear.Intro"], NativeTheme.Muted));

        if (_loading)
        {
            _body.Add(new ActivityIndicator { IsRunning = true, AutomationId = "creation-gear-loading" });
            _body.Add(NativeTheme.Body(CreationFlowStrings.Get("Gear.Loading", "Loading gear…"), NativeTheme.Muted));
            return;
        }
        if (_loaded is not { } load || _loadedDisplay is not { } original
            || !Coordinator.IsCreationCatalogDisplayCurrent(original))
        {
            AddBlockers(_copy["Gear.AuthorityUnavailable"],
                [CharacterCreationGearInteractionBlockers.BindingMismatch], "creation-gear-unavailable");
            return;
        }

        // Render only this appearance's validated snapshot. Preview/Confirm
        // retain fresh Core validation; browsing does not read or mutate Core.
        if (!string.Equals(load.Outcome, CharacterCreationGearOutcomes.Available, StringComparison.Ordinal)
            || load.State is not { } state)
        {
            AddBlockers(
                _copy["Gear.AuthorityUnavailable"],
                load.Blockers.DefaultIfEmpty(load.Outcome).ToArray(),
                "creation-gear-unavailable");
            return;
        }

        InitializeBasket(state);
        // The preceding commit remains inspectable in the existing collapsed
        // diagnostic details. Never show its receipt against a later revision.
        if (_resourcesReceipt is { } receipt && receipt.WorkspaceId == state.Binding.WorkspaceId
            && receipt.WorkspaceRevision == state.Binding.ContentRevision
            && receipt.SavedRevision == state.Binding.SavedRevision
            && receipt.DraftRevision == state.Binding.ResourcesDraftRevision
            && CharacterCreationResourcesRules.DigestsEqual(receipt.DraftDigest, state.Binding.ResourcesDraftDigest)
            && CharacterCreationResourcesRules.DigestsEqual(receipt.RawCharacterXmlDigest, state.Binding.RawCharacterXmlDigest))
            _technicalDetails.Add(CreationResourcesPreviewPage.CreateReceiptDetails(receipt));
        AddBinding(state);
        AddPersistedBudget(state);
        if (!_ready)
        {
            AddBlockers(
                _copy["Gear.AuthorityBlocked"],
                state.Blockers.DefaultIfEmpty(CharacterCreationGearBlockers.AuthorityUnavailable).ToArray(),
                "creation-gear-blockers");
            AddAuthority(state);
            return;
        }

        _body.Add(_basketBody);
        _technicalDetails.Add(_basketDetails);
        AddBasket(state);
        AddCatalog(state);
        AddAuthority(state);
    }

    private void InitializeBasket(CharacterCreationGearInteractionState state)
    {
        if (CharacterCreationGearRules.DigestsEqual(_initializedSnapshotDigest, state.SnapshotDigest))
            return;

        _basket = state.PendingDraft?.Lines.ToDictionary(
            line => line.OptionId,
            line => line.Quantity,
            StringComparer.Ordinal) ?? new Dictionary<string, int>(StringComparer.Ordinal);
        _initializedSnapshotDigest = state.SnapshotDigest;
        _catalogOffset = 0;
    }

    private void AddBinding(CharacterCreationGearInteractionState state)
    {
        Label binding = NativeTheme.Body(_copy.Format(
            "Gear.Binding",
            state.Binding.ContentRevision,
            state.Binding.SavedRevision,
            ShortDigest(state.SnapshotDigest),
            ShortDigest(state.Binding.SourceDigest)), NativeTheme.Muted);
        binding.AutomationId = "creation-gear-binding";
        _technicalDetails.Add(binding);
        _technicalDetails.Add(ExactValue("creation-gear-binding-workspace-revision", state.Binding.WorkspaceRevision));
        _technicalDetails.Add(ExactValue("creation-gear-binding-content-revision", state.Binding.ContentRevision));
        _technicalDetails.Add(ExactValue("creation-gear-binding-saved-revision", state.Binding.SavedRevision));
        _technicalDetails.Add(ExactValue("creation-gear-binding-resources-draft-revision", state.Binding.ResourcesDraftRevision));
        _technicalDetails.Add(ExactValue("creation-gear-binding-raw-character-xml-digest", state.Binding.RawCharacterXmlDigest));
        _technicalDetails.Add(ExactValue("creation-gear-binding-auxiliary-state-digest", state.Binding.AuxiliaryStateDigest));
        _technicalDetails.Add(ExactValue("creation-gear-binding-resources-draft-digest", state.Binding.ResourcesDraftDigest));
        _technicalDetails.Add(ExactValue("creation-gear-binding-snapshot-digest", state.SnapshotDigest));
    }

    private void AddPersistedBudget(CharacterCreationGearInteractionState state)
    {
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Eyebrow(_copy["Gear.PersistedDraft"]));
        card.Add(NativeTheme.Metric(_copy["Gear.StartingNuyen"], Nuyen(state.Budget.TotalStartingNuyen)));
        card.Add(NativeTheme.Metric(_copy["Gear.BasketCost"], Nuyen(state.Budget.BasketCost)));
        card.Add(NativeTheme.Metric(_copy["Common.Remaining"], Nuyen(state.Budget.RemainingNuyen)));
        card.Add(NativeTheme.Metric(_copy["Common.Lines"], (state.PendingDraft?.Lines.Count ?? 0).ToString(_copy.DisplayCulture)));
        card.Add(NativeTheme.Body(
            state.PendingDraft is null
                ? _copy["Gear.NoConfirmedBasket"]
                : CreationFlowStrings.Get("Gear.SavedBasket", "Your equipment choices are saved."),
            NativeTheme.Muted));
        _technicalDetails.Add(ExactValue("creation-gear-saved-basket-cost", state.Budget.BasketCost));
        _technicalDetails.Add(ExactValue("creation-gear-saved-remaining-nuyen", state.Budget.RemainingNuyen));
        if (state.PendingDraft is { } pending)
        {
            _technicalDetails.Add(ExactValue("creation-gear-saved-draft-revision", pending.DraftRevision));
            _technicalDetails.Add(ExactValue("creation-gear-saved-draft-digest", pending.DraftDigest));
        }
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-gear-saved-draft";
        _body.Add(border);
    }

    private void AddBasket(CharacterCreationGearInteractionState state)
    {
        _basketBody.Clear();
        _basketDetails.Clear();
        _basketBody.Add(NativeTheme.Eyebrow(_copy["Gear.DraftBasket"]));
        if (_basket.Count == 0)
        {
            Label empty = NativeTheme.Body(_copy["Gear.NoCatalogLines"], NativeTheme.Muted);
            empty.AutomationId = "creation-gear-basket-empty";
            _basketBody.Add(NativeTheme.Card(empty));
        }
        else
        {
            Dictionary<string, CharacterCreationGearCatalogOption> catalog = state.Authority.Options
                .ToDictionary(option => option.OptionId, StringComparer.Ordinal);
            foreach ((string optionId, int quantity) in _basket.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                if (!catalog.TryGetValue(optionId, out CharacterCreationGearCatalogOption? option)
                    || !option.IsSelectable
                    || option.Blockers.Count != 0)
                {
                    AddBlockers(
                        _copy["Gear.BasketAuthorityChanged"],
                        [CharacterCreationGearBlockers.InvalidOption],
                        $"creation-gear-basket-invalid-{Token(optionId)}", _basketBody);
                    continue;
                }
                _basketBody.Add(BasketLine(state, option, quantity));
            }
        }

        bool differs = CreationGearPhoneBasket.DiffersFromPersisted(_basket, state.PendingDraft);
        if (!differs && state.PendingDraft is not null)
        {
            var next = NativeTheme.PrimaryButton(CreationFlowStrings.Get("Gear.ContinueNext", "Continue to contacts"));
            next.AutomationId = "creation-gear-saved-continue";
            long appearance = CaptureAppearanceGeneration();
            next.Clicked += async (_, _) => await RunAsync(() => ContinueSavedAsync(state, next, appearance));
            _basketBody.Add(next);
            return;
        }
        Button preview = NativeTheme.PrimaryButton(_copy["Gear.ReviewBasket"]);
        preview.AutomationId = "creation-gear-preview";
        preview.IsEnabled = differs;
        preview.Clicked += async (_, _) => await RunAsync(() => OpenPreviewAsync(state));
        _basketBody.Add(preview);
        Label previewAuthority = NativeTheme.Body(
            differs
                ? _copy["Gear.CoreWillCalculate"]
                : _copy["Gear.ChangeBasket"],
            NativeTheme.Muted);
        previewAuthority.AutomationId = "creation-gear-preview-authority";
        _basketBody.Add(previewAuthority);
    }

    private Border BasketLine(
        CharacterCreationGearInteractionState state,
        CharacterCreationGearCatalogOption option,
        int quantity)
    {
        VerticalStackLayout content = new() { Spacing = 7 };
        content.Add(NativeTheme.Title(option.Name, 18));
        content.Add(NativeTheme.Body(_copy.Format(
            "Gear.PackageDetail",
            option.Category,
            option.PackageCost,
            option.PackageQuantity,
            option.Legality), NativeTheme.Muted));
        content.Add(NativeTheme.Metric(_copy["Gear.Quantity"], quantity.ToString(_copy.DisplayCulture)));
        _basketDetails.Add(ExactValue($"creation-gear-basket-{Token(option.OptionId)}-option-id", option.OptionId));
        _basketDetails.Add(ExactValue($"creation-gear-basket-{Token(option.OptionId)}-quantity", quantity));

        Border card = NativeTheme.Card(content);
        card.AutomationId = $"creation-gear-basket-{Token(option.OptionId)}";
        void SetQuantity(int next)
        {
            if (ReferenceEquals(card.Parent, _basketBody)) UpdateQuantity(state, option.OptionId, next);
        }
        HorizontalStackLayout actions = new() { Spacing = 8 };
        Button decrement = NativeTheme.SecondaryButton("−");
        decrement.AutomationId = $"creation-gear-basket-{Token(option.OptionId)}-decrement";
        decrement.Clicked += (_, _) => SetQuantity(quantity - 1);
        actions.Add(decrement);
        Button increment = NativeTheme.SecondaryButton("+");
        increment.AutomationId = $"creation-gear-basket-{Token(option.OptionId)}-increment";
        increment.IsEnabled = quantity < state.Authority.MaximumQuantityPerLine;
        increment.Clicked += (_, _) => SetQuantity(quantity + 1);
        actions.Add(increment);
        Button remove = NativeTheme.SecondaryButton(_copy["Gear.Remove"]);
        remove.AutomationId = $"creation-gear-basket-{Token(option.OptionId)}-remove";
        remove.Clicked += (_, _) => SetQuantity(0);
        actions.Add(remove);
        content.Add(actions);
        return card;
    }

    private void AddCatalog(CharacterCreationGearInteractionState state)
    {
        _body.Add(NativeTheme.Eyebrow(_copy["Gear.ActiveCatalog"]));
        _body.Add(NativeTheme.Body(CreationFlowStrings.Get("Gear.AvailableCatalog",
            "Only equipment available for this character is shown. Review your basket to check its total cost."), NativeTheme.Muted));
        SearchBar search = new()
        {
            AutomationId = "creation-gear-search",
            Placeholder = _copy["Gear.Search"],
            Text = _filter,
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text,
            PlaceholderColor = NativeTheme.Muted
        };
        search.SearchButtonPressed += (_, _) =>
        {
            if (!ReferenceEquals(search.Parent, _body) || !IsCatalogCurrent()) return;
            search.Unfocus();
            ApplyFilter(search.Text);
        };
        search.TextChanged += (_, args) =>
        {
            if (ReferenceEquals(search.Parent, _body) && IsCatalogCurrent()
                && string.IsNullOrWhiteSpace(args.NewTextValue) && !string.IsNullOrWhiteSpace(_filter))
                ApplyFilter(string.Empty);
        };
        _body.Add(search);

        // Derive types from the admitted catalog, not the current search/page or
        // a second rules read. A type never admits previously unavailable gear.
        string[] categories = state.Authority.Options.Where(IsCatalogOptionAvailable)
            .Select(option => option.Category).Distinct(StringComparer.Ordinal)
            .OrderBy(CategoryName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(category => category, StringComparer.Ordinal).ToArray();
        if (_category is not null && !categories.Contains(_category, StringComparer.Ordinal)) _category = null;
        Picker type = new()
        {
            AutomationId = "creation-gear-type",
            Title = CreationFlowStrings.Get("Gear.Filter.Type", "Equipment type"),
            ItemsSource = new[] { CreationFlowStrings.Get("Gear.Filter.All", "All equipment types") }
                .Concat(categories.Select(CategoryName)).ToArray(),
            SelectedIndex = _category is null ? 0 : Array.IndexOf(categories, _category) + 1,
            TextColor = NativeTheme.Text,
            BackgroundColor = NativeTheme.Surface
        };
        SemanticProperties.SetDescription(type, type.Title);
        type.SelectedIndexChanged += (_, _) =>
        {
            if (!ReferenceEquals(type.Parent, _body) || !IsCatalogCurrent()
                || type.SelectedIndex < 0 || type.SelectedIndex > categories.Length) return;
            _category = type.SelectedIndex == 0 ? null : categories[type.SelectedIndex - 1];
            _catalogOffset = 0;
            // The native Picker dismisses its dialog only after this callback
            // returns. Do not rebuild dozens of native controls inside it.
            CatalogRefresh = RefreshCatalogAfterPickerAsync(state, type,
                CaptureAppearanceGeneration(), ++_catalogRefreshGeneration);
        };
        _body.Add(type);
        _technicalDetails.Add(_catalogDetails);
        _body.Add(_catalog);
        RenderCatalog(state);
    }

    private static string CategoryName(string category)
        => CreationFlowStrings.Get("Gear.Type." + category, category);

    private async Task RefreshCatalogAfterPickerAsync(CharacterCreationGearInteractionState state,
        Picker type, long appearance, long request)
    {
        await Task.Yield();
        if (request != _catalogRefreshGeneration || !IsCurrentAppearanceGeneration(appearance)
            || !ReferenceEquals(type.Parent, _body) || !ReferenceEquals(state, _loaded?.State)
            || !IsCatalogCurrent()) return;
        RenderCatalog(state);
    }

    private bool IsCatalogCurrent() => ReferenceEquals(_catalog.Parent, _body)
        && IsCurrentAppearanceGeneration(CaptureAppearanceGeneration())
        && _loadedDisplay is { } original && Coordinator.IsCreationCatalogDisplayCurrent(original);

    private void RenderCatalog(CharacterCreationGearInteractionState state)
    {
        _catalogRefreshGeneration++;
        _catalog.Clear();
        _catalogDetails.Clear();
        _catalogRows.Clear();

        CharacterCreationGearCatalogOption[] matches = state.Authority.Options
            .Where(IsCatalogOptionAvailable)
            .Where(option => _category is null || string.Equals(option.Category, _category, StringComparison.Ordinal))
            .Where(MatchesFilter)
            .OrderBy(option => CategoryName(option.Category), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(option => option.Name, StringComparer.Ordinal)
            .ThenBy(option => option.OptionId, StringComparer.Ordinal)
            .ToArray();
        _catalogOffset = Math.Min(_catalogOffset, Math.Max(0, matches.Length - 1) / CatalogPageSize * CatalogPageSize);
        int end = Math.Min(matches.Length, _catalogOffset + CatalogPageSize);
        Label range = NativeTheme.Body(
            matches.Length == 0
                ? _copy["Gear.NoMatches"]
                : _copy.Format("Gear.Showing", _catalogOffset + 1, end, matches.Length),
            NativeTheme.Muted);
        range.AutomationId = "creation-gear-catalog-range";
        _catalog.Add(range);

        foreach (CharacterCreationGearCatalogOption option in matches.Skip(_catalogOffset).Take(CatalogPageSize))
        {
            bool enabled = CanAddCatalogOption(state, option);
            string detail = _copy.Format(
                    "Gear.CatalogDetail",
                    CategoryName(option.Category),
                    option.PackageCost,
                    option.PackageQuantity,
                    option.Availability,
                    option.Legality,
                    option.SourceBook,
                    option.Page);
            Border? row = null;
            row = NativeTheme.NavigationRow(
                option.Name,
                detail,
                () =>
                {
                    if (!ReferenceEquals(row?.Parent, _catalog) || !IsCatalogCurrent()) return Task.CompletedTask;
                    int next = _basket.TryGetValue(option.OptionId, out int current) ? current + 1 : 1;
                    UpdateQuantity(state, option.OptionId, next);
                    return Task.CompletedTask;
                },
                enabled,
                $"creation-gear-catalog-{Token(option.OptionId)}");
            _catalog.Add(row);
            _catalogRows.Add((option, row));
            _catalogDetails.Add(ExactValue($"creation-gear-catalog-{Token(option.OptionId)}-option-id", option.OptionId));
            _catalogDetails.Add(ExactValue($"creation-gear-catalog-{Token(option.OptionId)}-option-digest", option.OptionDigest));
        }

        HorizontalStackLayout pager = new() { Spacing = 10 };
        Button previous = NativeTheme.SecondaryButton(_copy["Common.Previous"]);
        previous.AutomationId = "creation-gear-catalog-previous";
        previous.IsEnabled = _catalogOffset > 0;
        previous.Clicked += (_, _) =>
        {
            if (!ReferenceEquals(pager.Parent, _catalog) || !IsCatalogCurrent()) return;
            _catalogOffset = Math.Max(0, _catalogOffset - CatalogPageSize);
            RenderCatalog(state);
        };
        pager.Add(previous);
        Button next = NativeTheme.SecondaryButton(_copy["Common.Next"]);
        next.AutomationId = "creation-gear-catalog-next";
        next.IsEnabled = end < matches.Length;
        next.Clicked += (_, _) =>
        {
            if (!ReferenceEquals(pager.Parent, _catalog) || !IsCatalogCurrent()) return;
            _catalogOffset += CatalogPageSize;
            RenderCatalog(state);
        };
        pager.Add(next);
        _catalog.Add(pager);
    }

    private void ApplyFilter(string? value)
    {
        if (!IsCatalogCurrent() || _loaded?.State is not { } state) return;
        _filter = value?.Trim() ?? string.Empty;
        _catalogOffset = 0;
        RenderCatalog(state);
    }

    private bool MatchesFilter(CharacterCreationGearCatalogOption option)
    {
        if (string.IsNullOrWhiteSpace(_filter))
            return true;
        return option.Name.Contains(_filter, StringComparison.CurrentCultureIgnoreCase)
               || option.Category.Contains(_filter, StringComparison.CurrentCultureIgnoreCase)
               || CategoryName(option.Category).Contains(_filter, StringComparison.CurrentCultureIgnoreCase)
               || option.SourceBook.Contains(_filter, StringComparison.CurrentCultureIgnoreCase)
               || option.Legality.Contains(_filter, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateQuantity(
        CharacterCreationGearInteractionState state,
        string optionId,
        int quantity)
    {
        // Retained controls may belong to a departed appearance or account.
        if (_continueLifetime is not null || !_ready || !ReferenceEquals(state, _loaded?.State)
            || _loadedDisplay is not { } original
            || !Coordinator.IsCreationCatalogDisplayCurrent(original)) return;
        if (!CreationGearPhoneBasket.TrySetQuantity(
                _basket,
                optionId,
                quantity,
                state.Authority.MaximumBasketLines,
                state.Authority.MaximumQuantityPerLine,
                out Dictionary<string, int> updated))
            return;
        _basket = updated;
        // Quantity edits change the pending basket, not the catalog authority.
        // Preserve the catalog's native controls, search focus and scroll state.
        AddBasket(state);
        foreach ((var option, var row) in _catalogRows)
        {
            bool enabled = CanAddCatalogOption(state, option);
            Grid content = (Grid)row.Content!;
            content.Children.OfType<Button>().Single().IsEnabled = enabled;
            content.Children.OfType<Label>().Single().TextColor = enabled ? NativeTheme.Muted : NativeTheme.Line;
            row.Opacity = enabled ? 1 : 0.55;
        }
    }

    private bool CanAddCatalogOption(CharacterCreationGearInteractionState state,
        CharacterCreationGearCatalogOption option)
    {
        bool alreadySelected = _basket.TryGetValue(option.OptionId, out int quantity);
        return IsCatalogOptionAvailable(option)
            && (alreadySelected || _basket.Count < state.Authority.MaximumBasketLines)
            && (!alreadySelected || quantity < state.Authority.MaximumQuantityPerLine);
    }

    // Use Core's issued eligibility without recalculating rules in the renderer.
    // Basket limits only disable a retained row; removing a line can re-enable it.
    // Persisted basket entries remain visible and validated separately above.
    private static bool IsCatalogOptionAvailable(CharacterCreationGearCatalogOption option) =>
        option.IsSelectable && option.PricingIsExact && option.AvailabilityIsExact
        && option.Blockers.Count == 0;

    private async Task OpenPreviewAsync(CharacterCreationGearInteractionState state)
    {
        if (_continueLifetime is not null) return;
        if (!CreationGearPhoneBasket.TryCreateSelections(
                _basket,
                state.Authority.MaximumBasketLines,
                state.Authority.MaximumQuantityPerLine,
                out CharacterCreationGearSelection[] basket))
        {
            await DisplayAlertAsync(
                _copy["Gear.PreviewUnavailable"],
                CharacterCreationGearBlockers.InvalidBasket,
                _copy["Common.Ok"]);
            return;
        }

        if (_loadedDisplay is not { } original || !ReferenceEquals(state, _loaded?.State)
            || !Coordinator.IsCreationCatalogDisplayCurrent(original)) return;
        var review = await Task.Run(() => Coordinator.ReadCreationAuthority(original, () =>
        {
            var candidate = _gear.Prepare(original, basket);
            return (Result: candidate, Exact: candidate.PreparedPreview is { } preview
                && CreationGearPhoneAuthority.PreparedMatches(preview, state, original));
        }, CancellationToken.None));
        var result = review.Result;
        if (result.PreparedPreview is not { } prepared
            || !string.Equals(result.Outcome, CharacterCreationGearOutcomes.Available, StringComparison.Ordinal)
            || !review.Exact || !Coordinator.IsCreationCatalogDisplayCurrent(original))
        {
            string blocker = result.Blockers.FirstOrDefault()
                             ?? CharacterCreationGearInteractionBlockers.PreparedPreviewMismatch;
            await DisplayAlertAsync(_copy["Gear.PreviewUnavailable"], blocker, _copy["Common.Ok"]);
            Refresh();
            return;
        }
        await Navigation.PushAsync(new CreationGearPreviewPage(Coordinator, _gear, _overview, prepared, _copy, original));
    }

    private async Task ContinueSavedAsync(CharacterCreationGearInteractionState state, Button next, long appearance)
    {
        if (_continueLifetime is not null || _loadedDisplay is not { } display) return;
        var navigation = Navigation;
        var stack = navigation.NavigationStack.ToArray();
        bool Current() => _ready && IsCurrentAppearanceGeneration(appearance)
            && ReferenceEquals(state, _loaded?.State) && state.PendingDraft is not null
            && ReferenceEquals(next.Parent, _basketBody)
            && !CreationGearPhoneBasket.DiffersFromPersisted(_basket, state.PendingDraft)
            && Coordinator.IsCreationCatalogDisplayCurrent(display)
            && ReferenceEquals(stack.LastOrDefault(), this) && navigation.NavigationStack.SequenceEqual(stack);
        if (!Current()) return;
        using var lifetime = new CancellationTokenSource();
        _continueLifetime = lifetime;
        next.IsEnabled = false;
        next.Text = CreationFlowStrings.Get("Gear.OpeningNext", "Opening contacts…");
        bool unavailable = false;
        try
        {
            var continuation = await Coordinator.PrepareSavedCreationGearNextPageAsync(_gear, display, state, lifetime.Token);
            if (!Current() || lifetime.IsCancellationRequested) return;
            if (continuation is null || !Coordinator.IsCreationCatalogDisplayCurrent(continuation.Display))
            { unavailable = true; return; }
            await navigation.PushAsync(continuation.Page, animated: false);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) when (error is not OutOfMemoryException) { unavailable = true; }
        finally
        {
            if (ReferenceEquals(_continueLifetime, lifetime)) _continueLifetime = null;
            if (Current())
            {
                next.IsEnabled = true;
                next.Text = CreationFlowStrings.Get("Gear.ContinueNext", "Continue to contacts");
                if (unavailable)
                {
                    const string id = "creation-gear-saved-next-unavailable";
                    if (!_basketBody.Children.OfType<Label>().Any(x => x.AutomationId == id))
                    {
                        var feedback = NativeTheme.Body(CreationFlowStrings.Get("Gear.NextUnavailable", "Your equipment is saved. Try Continue again."), NativeTheme.Muted);
                        feedback.AutomationId = id;
                        _basketBody.Add(feedback);
                    }
                }
            }
        }
    }

    private void AddAuthority(CharacterCreationGearInteractionState state)
    {
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Eyebrow(_copy["Gear.CoreAuthority"]));
        card.Add(NativeTheme.Metric(_copy["Common.Profile"], state.Authority.SettingsProfileId));
        card.Add(NativeTheme.Metric(_copy["Gear.CatalogRows"], state.Authority.Options.Count.ToString(_copy.DisplayCulture)));
        card.Add(NativeTheme.Metric(_copy["Gear.MaximumAvailability"], state.Authority.MaximumAvailability.ToString(_copy.DisplayCulture)));
        card.Add(NativeTheme.Metric(_copy["Gear.MaximumBasketLines"], state.Authority.MaximumBasketLines.ToString(_copy.DisplayCulture)));
        card.Add(NativeTheme.Metric(_copy["Gear.MaximumQuantity"], state.Authority.MaximumQuantityPerLine.ToString(_copy.DisplayCulture)));
        _technicalDetails.Add(ExactValue("creation-gear-authority-digest", state.Binding.AuthorityDigest));
        _technicalDetails.Add(ExactValue("creation-gear-source-digest", state.Binding.SourceDigest));
        _technicalDetails.Add(ExactValue("creation-gear-rules-digest", state.Binding.RulesDigest));
        _technicalDetails.Add(ExactValue("creation-gear-runtime-digest", state.Binding.RuntimeDigest));
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-gear-authority";
        _technicalDetails.Add(border);
        _body.Add(NativeTheme.TechnicalDetails(_technicalDetails, "creation-gear-details"));
    }

    private void AddBlockers(string title, IReadOnlyList<string> blockers, string automationId,
        VerticalStackLayout? target = null)
    {
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Eyebrow(title));
        foreach (string blocker in blockers.Distinct(StringComparer.Ordinal))
            card.Add(NativeTheme.Body(blocker, NativeTheme.Danger));
        Border border = NativeTheme.Card(card);
        border.AutomationId = automationId;
        (target ?? _body).Add(border);
    }

    private string Nuyen(decimal value) => value.ToString("N0", _copy.DisplayCulture) + " ¥";
    private string ShortDigest(string value) =>
        string.IsNullOrWhiteSpace(value) ? _copy["Common.Unavailable"] : value[..Math.Min(19, value.Length)];
    private static string Token(string value) => new string(value.Select(character => char.IsLetterOrDigit(character)
        ? char.ToLowerInvariant(character)
        : '-').ToArray());
    private static Label ExactValue(string automationId, object value)
    {
        Label label = NativeTheme.Body(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty, NativeTheme.Muted);
        label.AutomationId = automationId;
        return label;
    }
}

public sealed class CreationGearPreviewPage : NativePageBase
{
    private readonly ICharacterCreationGearInteractionPresenter _gear;
    private readonly ICharacterOverviewPresenter _overview;
    private readonly CharacterCreationGearPreparedPreview _prepared;
    private readonly CharacterOverviewState _original;
    private bool _ready;
    private bool _loading = true;
    private bool _submitted;
    private bool _saving;
    private CancellationTokenSource? _forwardLifetime;
    private bool _nextStepUnavailable;
    private Button? _confirm;
    private Label? _confirmationStatus;
    private ActivityIndicator? _saveProgress;
    private readonly AndroidSurfaceCopy _copy;
    private VerticalStackLayout _technicalDetails = new() { Spacing = 6 };
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };
    private CharacterCreationGearReceipt? _receipt;
    private string? _failure;

    public CreationGearPreviewPage(
        RunnerSessionCoordinator coordinator,
        ICharacterCreationGearInteractionPresenter gear,
        ICharacterOverviewPresenter overview,
        CharacterCreationGearPreparedPreview prepared,
        AndroidSurfaceCopy copy, CharacterOverviewState? original = null) : base(coordinator)
    {
        _gear = gear ?? throw new ArgumentNullException(nameof(gear));
        _overview = overview ?? throw new ArgumentNullException(nameof(overview));
        _prepared = prepared ?? throw new ArgumentNullException(nameof(prepared));
        _original = original ?? coordinator.State;
        _copy = copy ?? throw new ArgumentNullException(nameof(copy));
        Title = _copy["GearPreview.PageTitle"];
        AutomationId = "creation-gear-preview-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void OnDisappearing()
    {
        // Navigation cancels only the read preparing the next step, never the
        // already-submitted durable purchase.
        _forwardLifetime?.Cancel();
        base.OnDisappearing();
    }

    protected override async Task PrepareForAppearanceRefreshAsync(CancellationToken ct)
    {
        _ready = false;
        _loading = true;
        Refresh();
        try
        {
            if (_submitted || !Coordinator.IsCreationCatalogDisplayCurrent(_original)) return;
            _ready = await Task.Run(() => Coordinator.ReadCreationAuthority(_original, () =>
            {
                var load = _gear.Load(_original);
                return load.State is { } current
                    && CreationGearPhoneAuthority.PreparedMatches(_prepared, current, _original);
            }, ct), ct);
        }
        finally { if (!ct.IsCancellationRequested) _loading = false; }
    }

    protected override void Refresh()
    {
        // Keep the issued, possibly scrolled confirmation controls stable while
        // saving. A changed account/runner must still hide the private preview.
        if ((_saving || _forwardLifetime is not null) && Coordinator.CanDisplayCreationPurchase(_original)) return;
        if (_saveProgress is { } previousProgress)
            previousProgress.IsVisible = previousProgress.IsRunning = false;
        _confirm = null;
        _confirmationStatus = null;
        _saveProgress = null;
        _body.Clear();
        _technicalDetails = new() { Spacing = 6 };
        _body.Add(NativeTheme.Title(_copy["GearPreview.Title"]));
        if (!Coordinator.CanDisplayCreationPurchase(_original))
        {
            _body.Add(NativeTheme.Body(_copy["GearPreview.ConfirmStale"], NativeTheme.Danger));
            return;
        }
        if (_loading)
        {
            _body.Add(new ActivityIndicator { IsRunning = true, AutomationId = "creation-gear-preview-loading" });
            return;
        }
        AddExactPreview();

        if (_receipt is { } receipt)
        {
            AddReceipt(receipt);
            Button next = NativeTheme.PrimaryButton(CreationFlowStrings.Get("Gear.ContinueNext", "Continue to contacts"));
            next.AutomationId = "creation-gear-continue-next";
            next.IsEnabled = CanContinueSaved();
            long savedAppearance = CaptureAppearanceGeneration();
            next.Clicked += async (_, _) =>
            {
                if (ReferenceEquals(_confirm, next) && IsCurrentAppearanceGeneration(savedAppearance))
                    await RunAsync(() => ContinueToNextAsync(savedAppearance));
            };
            _confirm = next;
            _body.Add(next);
            _body.Add(NativeTheme.TechnicalDetails(_technicalDetails, "creation-gear-preview-details"));
            return;
        }

        if (!string.IsNullOrWhiteSpace(_failure))
        {
            Label failure = NativeTheme.Body(_failure, NativeTheme.Danger);
            failure.AutomationId = "creation-gear-confirm-failed";
            _body.Add(NativeTheme.Card(failure));
        }

        bool exact = !_submitted && _ready && Coordinator.IsCreationCatalogDisplayCurrent(_original);
        Button confirm = NativeTheme.PrimaryButton(_copy["GearPreview.Confirm"]);
        confirm.AutomationId = "creation-gear-confirm";
        confirm.IsEnabled = exact;
        long appearance = CaptureAppearanceGeneration();
        confirm.Clicked += async (_, _) =>
        {
            if (ReferenceEquals(_confirm, confirm) && IsCurrentAppearanceGeneration(appearance))
                await RunAsync(ConfirmAsync);
        };
        _confirm = confirm;
        _body.Add(confirm);
        Label authority = NativeTheme.Body(
            exact
                ? _copy["GearPreview.ConfirmExact"]
                : _copy["GearPreview.ConfirmStale"],
            exact ? NativeTheme.Muted : NativeTheme.Danger);
        authority.AutomationId = "creation-gear-confirm-authority";
        _confirmationStatus = authority;
        _body.Add(authority);
        _saveProgress = new ActivityIndicator
        {
            IsVisible = false, IsRunning = false,
            AutomationId = "creation-gear-save-progress"
        };
        _body.Add(_saveProgress);
        _body.Add(NativeTheme.TechnicalDetails(_technicalDetails, "creation-gear-preview-details"));
    }

    private void AddExactPreview()
    {
        VerticalStackLayout budget = new() { Spacing = 6 };
        budget.Add(NativeTheme.Eyebrow(_copy["GearPreview.ExactProjection"]));
        budget.Add(NativeTheme.Metric(_copy["GearPreview.BasketBefore"], Nuyen(_prepared.Preview.BudgetBefore.BasketCost)));
        budget.Add(NativeTheme.Metric(_copy["GearPreview.BasketAfter"], Nuyen(_prepared.Preview.BudgetAfter.BasketCost)));
        budget.Add(NativeTheme.Metric(_copy["Common.Remaining"], Nuyen(_prepared.Preview.BudgetAfter.RemainingNuyen)));
        budget.Add(NativeTheme.Metric(_copy["Common.Lines"], _prepared.Preview.After.Lines.Count.ToString(_copy.DisplayCulture)));
        _technicalDetails.Add(ExactValue("creation-gear-preview-basket-cost", _prepared.Preview.BudgetAfter.BasketCost));
        _technicalDetails.Add(ExactValue("creation-gear-preview-remaining-nuyen", _prepared.Preview.BudgetAfter.RemainingNuyen));
        _technicalDetails.Add(ExactValue("creation-gear-preview-digest", _prepared.Preview.PreviewDigest));
        _technicalDetails.Add(ExactValue("creation-gear-preview-state-snapshot-digest", _prepared.StateSnapshotDigest));
        Border budgetCard = NativeTheme.Card(budget);
        budgetCard.AutomationId = "creation-gear-preview-budget";
        _body.Add(budgetCard);

        foreach (CharacterCreationGearLine line in _prepared.Preview.After.Lines)
        {
            VerticalStackLayout content = new() { Spacing = 5 };
            content.Add(NativeTheme.Title(line.Name, 18));
            content.Add(NativeTheme.Body(_copy.Format(
                "GearPreview.LineDetail",
                line.Category,
                line.Quantity,
                Nuyen(line.TotalCost),
                line.Availability,
                line.Legality,
                line.SourceBook,
                line.Page), NativeTheme.Muted));
            _technicalDetails.Add(ExactValue($"creation-gear-preview-line-{Token(line.OptionId)}-option-id", line.OptionId));
            _technicalDetails.Add(ExactValue($"creation-gear-preview-line-{Token(line.OptionId)}-quantity", line.Quantity));
            _technicalDetails.Add(ExactValue($"creation-gear-preview-line-{Token(line.OptionId)}-digest", line.LineDigest));
            Border lineCard = NativeTheme.Card(content);
            lineCard.AutomationId = $"creation-gear-preview-line-{Token(line.OptionId)}";
            _body.Add(lineCard);
        }
    }

    private async Task ConfirmAsync()
    {
        long appearance = CaptureAppearanceGeneration();
        if (_submitted || !_ready || !IsCurrentAppearanceGeneration(appearance)
            || !ReferenceEquals(Navigation.NavigationStack.LastOrDefault(), this)
            || !Coordinator.IsCreationCatalogDisplayCurrent(_original)) return;
        _submitted = true;
        _ready = false;
        _failure = null;
        _saving = true;
        if (_confirm is { } confirm)
        {
            confirm.IsEnabled = false;
            confirm.Text = CreationFlowStrings.Get("Gear.Saving", "Saving gear…");
        }
        if (_confirmationStatus is { } status)
        {
            status.Text = CreationFlowStrings.Get("Gear.SavingDetail", "Saving your equipment choices. Please wait…");
            status.TextColor = NativeTheme.Muted;
        }
        ActivityIndicator? progress = _saveProgress;
        if (progress is not null) progress.IsVisible = progress.IsRunning = true;
        try
        {
            var result = await Coordinator.ConfirmCreationGearPurchaseAsync(_gear, _original, _prepared);
            if (result.Receipt is not { } receipt
                || result.Outcome is not (CharacterCreationGearOutcomes.Applied or CharacterCreationGearOutcomes.Replayed)
                || !CreationGearPhoneAuthority.ReceiptMatches(_prepared, receipt))
            {
                _failure = result.Outcome == "outcome-unknown"
                    ? CreationFlowStrings.Get("Purchases.OutcomeUnknown", "The result could not be verified. Reopen the character before making another purchase.")
                    : result.Blockers.FirstOrDefault() ?? CharacterCreationGearInteractionBlockers.ReceiptMismatch;
                return;
            }
            // Retain the durable receipt before refreshing. Refresh failure is not
            // mutation failure and must not permit a second submission.
            _receipt = receipt;
            if (result.RefreshedState is not { } refreshed
                || !CreationGearPhoneAuthority.RefreshedStateMatches(_prepared, receipt, refreshed)
                || !Coordinator.CanDisplayCreationPurchase(_original))
            {
                _failure = CreationFlowStrings.Get("Purchases.SavedReopen", "Saved. Reopen the character to refresh this view.");
                return;
            }
            await ContinueToNextAsync(appearance);
        }
        finally
        {
            _saving = false;
            if (progress is not null) progress.IsVisible = progress.IsRunning = false;
        }
    }

    private bool CanContinueSaved() => _receipt is not null
        && Coordinator.CanDisplayCreationPurchase(_original) && Coordinator.State.Profile?.Created == false;

    private async Task ContinueToNextAsync(long appearance)
    {
        if (_forwardLifetime is not null || !CanContinueSaved()) return;
        var navigation = Navigation;
        var stack = navigation.NavigationStack.ToArray();
        bool Current() => IsCurrentAppearanceGeneration(appearance) && CanContinueSaved()
            && ReferenceEquals(stack.LastOrDefault(), this) && navigation.NavigationStack.SequenceEqual(stack);
        if (!Current()) return;
        using var lifetime = new CancellationTokenSource();
        _forwardLifetime = lifetime;
        _nextStepUnavailable = false;
        if (_confirm is { } next)
        {
            next.IsEnabled = false;
            next.Text = CreationFlowStrings.Get("Gear.OpeningNext", "Opening contacts…");
        }
        if (_confirmationStatus is { } status)
            status.Text = CreationFlowStrings.Get("Gear.OpeningNext", "Opening contacts…");
        try
        {
            var continuation = await Coordinator.PrepareCreationAfterGearPageAsync(
                _gear, _original, _prepared, _receipt!, lifetime.Token);
            if (!Current() || lifetime.IsCancellationRequested) return;
            if (continuation is null || !Coordinator.IsCreationCatalogDisplayCurrent(continuation.Display))
            { _nextStepUnavailable = true; return; }
            var page = continuation.Page;
            await navigation.PushAsync(page, animated: false);
            if (navigation.NavigationStack.SequenceEqual(stack.Append(page))) navigation.RemovePage(this);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) when (error is not OutOfMemoryException)
        { if (Current()) _nextStepUnavailable = true; }
        finally
        {
            if (ReferenceEquals(_forwardLifetime, lifetime)) _forwardLifetime = null;
            if (!_saving) Refresh();
        }
    }

    private void AddReceipt(CharacterCreationGearReceipt receipt)
    {
        VerticalStackLayout content = new() { Spacing = 6 };
        content.Add(NativeTheme.Eyebrow(_copy["GearPreview.Persisted"]));
        if (_nextStepUnavailable) content.Add(NativeTheme.Body(
            CreationFlowStrings.Get("Gear.NextUnavailable", "Your equipment is saved. Contacts could not be opened. Try Continue again; this will not save twice."), NativeTheme.Muted));
        else if (_failure is not null) content.Add(NativeTheme.Body(_failure, NativeTheme.Muted));
        _technicalDetails.Add(NativeTheme.Metric(_copy["Common.Receipt"], receipt.ReceiptId));
        _technicalDetails.Add(NativeTheme.Metric(_copy["Common.WorkspaceRevision"], receipt.WorkspaceRevision.ToString(_copy.DisplayCulture)));
        _technicalDetails.Add(NativeTheme.Metric(_copy["Common.DraftRevision"], receipt.DraftRevision.ToString(_copy.DisplayCulture)));
        content.Add(NativeTheme.Metric(_copy["Gear.BasketCost"], Nuyen(receipt.BasketCost)));
        _technicalDetails.Add(CreateReceiptDetails(receipt));
        Border card = NativeTheme.Card(content);
        card.AutomationId = "creation-gear-confirm-receipt";
        _body.Add(card);
    }

    internal static View CreateReceiptDetails(CharacterCreationGearReceipt receipt)
    {
        var details = new VerticalStackLayout { Spacing = 6, AutomationId = "creation-gear-receipt-details" };
        details.Add(ExactValue("creation-gear-receipt-workspace-revision", receipt.WorkspaceRevision));
        details.Add(ExactValue("creation-gear-receipt-saved-revision", receipt.SavedRevision));
        details.Add(ExactValue("creation-gear-receipt-resources-draft-revision", receipt.ResourcesDraftRevision));
        details.Add(ExactValue("creation-gear-receipt-draft-revision", receipt.DraftRevision));
        details.Add(ExactValue("creation-gear-receipt-line-count", receipt.LineCount));
        details.Add(ExactValue("creation-gear-receipt-basket-cost", receipt.BasketCost));
        details.Add(ExactValue("creation-gear-receipt-remaining-nuyen", receipt.RemainingNuyen));
        details.Add(ExactValue("creation-gear-receipt-raw-character-xml-digest", receipt.RawCharacterXmlDigest));
        details.Add(ExactValue("creation-gear-receipt-resources-draft-digest", receipt.ResourcesDraftDigest));
        details.Add(ExactValue("creation-gear-receipt-command-digest", receipt.CommandDigest));
        details.Add(ExactValue("creation-gear-receipt-preview-digest", receipt.PreviewDigest));
        details.Add(ExactValue("creation-gear-receipt-draft-digest", receipt.DraftDigest));
        details.Add(ExactValue("creation-gear-receipt-digest", receipt.ReceiptDigest));
        return details;
    }

    private string Nuyen(decimal value) => value.ToString("N0", _copy.DisplayCulture) + " ¥";
    private static string Token(string value) => new string(value.Select(character => char.IsLetterOrDigit(character)
        ? char.ToLowerInvariant(character)
        : '-').ToArray());
    private static Label ExactValue(string automationId, object value)
    {
        Label label = NativeTheme.Body(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty, NativeTheme.Muted);
        label.AutomationId = automationId;
        return label;
    }
}

public static class CreationGearPhoneBasket
{
    public static bool TrySetQuantity(
        IReadOnlyDictionary<string, int> current,
        string optionId,
        int quantity,
        int maximumLines,
        int maximumQuantity,
        out Dictionary<string, int> updated)
    {
        updated = new Dictionary<string, int>(StringComparer.Ordinal);
        if (!IsValid(current, maximumLines, maximumQuantity)
            || string.IsNullOrWhiteSpace(optionId)
            || quantity < 0
            || quantity > maximumQuantity)
            return false;
        updated = current.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        if (quantity == 0)
            updated.Remove(optionId);
        else if (updated.ContainsKey(optionId) || updated.Count < maximumLines)
            updated[optionId] = quantity;
        else
            return false;
        return IsValid(updated, maximumLines, maximumQuantity);
    }

    public static bool TryCreateSelections(
        IReadOnlyDictionary<string, int> current,
        int maximumLines,
        int maximumQuantity,
        out CharacterCreationGearSelection[] selections)
    {
        selections = [];
        if (!IsValid(current, maximumLines, maximumQuantity))
            return false;
        selections = current.OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => new CharacterCreationGearSelection(item.Key, item.Value))
            .ToArray();
        return true;
    }

    public static bool DiffersFromPersisted(
        IReadOnlyDictionary<string, int> current,
        CharacterCreationGearDraft? persisted)
    {
        // No purchase is a valid first choice, but it still needs an explicit
        // Core draft/receipt before later Creation steps can proceed. An absent
        // draft must not be confused with an already confirmed empty basket.
        if (persisted is null)
            return true;
        CharacterCreationGearSelection[] existing = persisted?.Lines
            .OrderBy(line => line.OptionId, StringComparer.Ordinal)
            .Select(line => new CharacterCreationGearSelection(line.OptionId, line.Quantity))
            .ToArray() ?? [];
        CharacterCreationGearSelection[] selected = current
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => new CharacterCreationGearSelection(item.Key, item.Value))
            .ToArray();
        return !existing.SequenceEqual(selected);
    }

    private static bool IsValid(
        IReadOnlyDictionary<string, int>? current,
        int maximumLines,
        int maximumQuantity) => current is not null
        && maximumLines > 0
        && maximumQuantity > 0
        && current.Count <= maximumLines
        && current.All(item => !string.IsNullOrWhiteSpace(item.Key)
                               && item.Value >= 1
                               && item.Value <= maximumQuantity);
}

internal static class CreationGearPhoneAuthority
{
    public static bool IsReady(
        CharacterCreationGearInteractionState state,
        CharacterOverviewState overview) => IsBound(state, overview)
        && state.ResourcesDraft is not null
        && state.CanEdit
        && state.Blockers.Count == 0
        && state.Budget.IsExact
        && state.Budget.Blockers.Count == 0
        && CharacterCreationGearRules.IsValidAuthority(state.Authority)
        && state.Authority.Options.Any(option => option.IsSelectable && option.Blockers.Count == 0);

    public static bool PreparedMatches(
        CharacterCreationGearPreparedPreview prepared,
        CharacterCreationGearInteractionState state,
        CharacterOverviewState overview) => IsReady(state, overview)
        && CharacterCreationGearRules.DigestsEqual(prepared.StateSnapshotDigest, state.SnapshotDigest)
        && prepared.Preview.Binding == state.Binding
        && BasketMatchesAuthority(prepared.Basket, state.Authority)
        && DraftMatches(prepared.Preview.Before, state.PendingDraft)
        && prepared.Preview.After.Lines.Select(line => new CharacterCreationGearSelection(line.OptionId, line.Quantity))
            .SequenceEqual(prepared.Basket.OrderBy(item => item.OptionId, StringComparer.Ordinal))
        && prepared.Preview.After.FinalizationContribution == prepared.Preview.FinalizationContribution
        && prepared.Preview.After.Budget == prepared.Preview.BudgetAfter
        && prepared.Preview.RequiresExplicitConfirmation
        && prepared.Preview.CanConfirm
        && prepared.Preview.Blockers.Count == 0
        && CharacterCreationGearRules.IsCanonicalDigest(prepared.Preview.PreviewDigest)
        && CharacterCreationGearRules.DigestsEqual(
            prepared.Preview.PreviewDigest,
            CharacterCreationGearRules.ComputePreviewDigest(prepared.Preview))
        && prepared.IdempotencyKey is { Length: > 0 and <= 200 }
        && string.Equals(prepared.IdempotencyKey, prepared.IdempotencyKey.Trim(), StringComparison.Ordinal);

    public static bool ReceiptMatches(
        CharacterCreationGearPreparedPreview prepared,
        CharacterCreationGearReceipt receipt) =>
        string.Equals(receipt.Schema, CharacterCreationGearSchemas.ReceiptV1, StringComparison.Ordinal)
        && receipt.WorkspaceId == prepared.Preview.Binding.WorkspaceId
        && receipt.PreviousWorkspaceRevision == prepared.Preview.Binding.WorkspaceRevision
        && receipt.WorkspaceRevision == receipt.PreviousWorkspaceRevision + 1
        && receipt.PreviousSavedRevision == prepared.Preview.Binding.SavedRevision
        && receipt.SavedRevision == receipt.WorkspaceRevision
        && receipt.ResourcesDraftRevision == prepared.Preview.Binding.ResourcesDraftRevision
        && CharacterCreationGearRules.DigestsEqual(receipt.ResourcesDraftDigest, prepared.Preview.Binding.ResourcesDraftDigest)
        && CharacterCreationGearRules.DigestsEqual(receipt.RawCharacterXmlDigest, prepared.Preview.Binding.RawCharacterXmlDigest)
        && receipt.LineCount == prepared.Preview.After.Lines.Count
        && receipt.BasketCost == prepared.Preview.BudgetAfter.BasketCost
        && receipt.RemainingNuyen == prepared.Preview.BudgetAfter.RemainingNuyen
        && CharacterCreationGearRules.DigestsEqual(receipt.PreviewDigest, prepared.Preview.PreviewDigest)
        && CharacterCreationGearRules.DigestsEqual(receipt.AuthorityDigest, prepared.Preview.Binding.AuthorityDigest)
        && CharacterCreationGearRules.DigestsEqual(receipt.SourceDigest, prepared.Preview.Binding.SourceDigest)
        && CharacterCreationGearRules.DigestsEqual(receipt.RulesDigest, prepared.Preview.Binding.RulesDigest)
        && CharacterCreationGearRules.DigestsEqual(receipt.RuntimeDigest, prepared.Preview.Binding.RuntimeDigest)
        && !receipt.CharacterDocumentChanged
        && CharacterCreationGearRules.IsCanonicalDigest(receipt.CommandDigest)
        && CharacterCreationGearRules.IsCanonicalDigest(receipt.DraftDigest)
        && CharacterCreationGearRules.IsCanonicalDigest(receipt.ReceiptDigest)
        && CharacterCreationGearRules.DigestsEqual(
            receipt.ReceiptDigest,
            CharacterCreationGearRules.ComputeReceiptDigest(receipt));

    public static bool RefreshedStateMatches(
        CharacterCreationGearPreparedPreview prepared,
        CharacterCreationGearReceipt receipt,
        CharacterCreationGearInteractionState refreshed) =>
        refreshed.PendingDraft is { } draft
        && refreshed.Binding.WorkspaceId == receipt.WorkspaceId
        && refreshed.Binding.WorkspaceRevision == receipt.WorkspaceRevision
        && refreshed.Binding.ContentRevision == receipt.WorkspaceRevision
        && refreshed.Binding.SavedRevision == receipt.SavedRevision
        && refreshed.Binding.ResourcesDraftRevision == receipt.ResourcesDraftRevision
        && CharacterCreationGearRules.DigestsEqual(refreshed.Binding.RawCharacterXmlDigest, receipt.RawCharacterXmlDigest)
        && CharacterCreationGearRules.DigestsEqual(refreshed.Binding.RawCharacterXmlDigest, prepared.Preview.Binding.RawCharacterXmlDigest)
        && CharacterCreationGearRules.DigestsEqual(refreshed.Binding.ResourcesDraftDigest, receipt.ResourcesDraftDigest)
        && draft.DraftRevision == receipt.DraftRevision
        && CharacterCreationGearRules.DigestsEqual(draft.DraftDigest, receipt.DraftDigest)
        && CharacterCreationGearRules.DigestsEqual(
            draft.DraftDigest,
            CharacterCreationGearRules.ComputeDraftDigest(draft))
        && draft.Lines.Count == receipt.LineCount
        && draft.Budget.BasketCost == receipt.BasketCost
        && draft.Budget.RemainingNuyen == receipt.RemainingNuyen
        && BudgetsEqual(refreshed.Budget, draft.Budget)
        && !draft.CharacterEffectsApplied
        && refreshed.Budget.IsExact
        && refreshed.Blockers.Count == 0
        && CharacterCreationGearRules.IsCanonicalDigest(refreshed.SnapshotDigest);

    private static bool BudgetsEqual(CharacterCreationGearBudget left, CharacterCreationGearBudget right)
        // The persisted draft is deserialized: collection identity is not
        // budget identity. Compare every scalar and ordered blocker instead.
        => left.TotalStartingNuyen == right.TotalStartingNuyen
           && left.BasketCost == right.BasketCost
           && left.RemainingNuyen == right.RemainingNuyen
           && left.Overspend == right.Overspend
           && left.IsExact == right.IsExact
           && left.Blockers.SequenceEqual(right.Blockers, StringComparer.Ordinal);

    private static bool IsBound(
        CharacterCreationGearInteractionState state,
        CharacterOverviewState overview) => overview.Profile?.Created == false
        && overview.WorkspaceId is { } workspaceId
        && overview.CreationWizard is { CharacterCreated: false } wizard
        && state.Binding.WorkspaceId == workspaceId
        && state.Binding.WorkspaceRevision == overview.ContentRevision
        && state.Binding.ContentRevision == overview.ContentRevision
        && state.Binding.SavedRevision == overview.SavedRevision
        && wizard.WorkspaceRevision == overview.ContentRevision
        && string.Equals(wizard.WorkspaceId, workspaceId.Value, StringComparison.Ordinal)
        && CharacterCreationGearRules.DigestsEqual(state.Binding.RawCharacterXmlDigest, wizard.ContentDigest)
        && CharacterCreationGearRules.IsCanonicalDigest(state.SnapshotDigest)
        && CharacterCreationGearRules.IsCanonicalDigest(state.Binding.RawCharacterXmlDigest)
        && CreationPrerequisitePhoneAuthority.IsCanonicalAuxiliaryStateDigest(state.Binding.AuxiliaryStateDigest)
        && CharacterCreationGearRules.IsCanonicalDigest(state.Binding.ResourcesDraftDigest)
        && CharacterCreationGearRules.IsCanonicalDigest(state.Binding.AuthorityDigest)
        && CharacterCreationGearRules.IsCanonicalDigest(state.Binding.SourceDigest)
        && CharacterCreationGearRules.IsCanonicalDigest(state.Binding.RulesDigest)
        && CharacterCreationGearRules.IsCanonicalDigest(state.Binding.RuntimeDigest);

    private static bool BasketMatchesAuthority(
        IReadOnlyList<CharacterCreationGearSelection> basket,
        CharacterCreationGearAuthority authority)
    {
        if (basket.Count > authority.MaximumBasketLines)
            return false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        Dictionary<string, CharacterCreationGearCatalogOption> catalog = authority.Options
            .ToDictionary(option => option.OptionId, StringComparer.Ordinal);
        return basket.All(selection => selection is not null
            && selection.Quantity is >= 1
            && selection.Quantity <= authority.MaximumQuantityPerLine
            && seen.Add(selection.OptionId)
            && catalog.TryGetValue(selection.OptionId, out CharacterCreationGearCatalogOption? option)
            && option.IsSelectable
            && option.PricingIsExact
            && option.AvailabilityIsExact
            && option.Blockers.Count == 0);
    }

    private static bool DraftMatches(CharacterCreationGearDraft? left, CharacterCreationGearDraft? right) =>
        left is null && right is null
        || left is not null && right is not null
           && CharacterCreationGearRules.DigestsEqual(left.DraftDigest, right.DraftDigest);
}
