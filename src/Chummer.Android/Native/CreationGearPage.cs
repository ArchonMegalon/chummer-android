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
    private const int CatalogPageSize = 40;
    private readonly ICharacterCreationGearInteractionPresenter _gear;
    private readonly ICharacterOverviewPresenter _overview;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };
    private Dictionary<string, int> _basket = new(StringComparer.Ordinal);
    private string? _initializedSnapshotDigest;
    private string _filter = string.Empty;
    private int _catalogOffset;

    public CreationGearPage(
        RunnerSessionCoordinator coordinator,
        ICharacterCreationGearInteractionPresenter gear,
        ICharacterOverviewPresenter overview) : base(coordinator)
    {
        _gear = gear ?? throw new ArgumentNullException(nameof(gear));
        _overview = overview ?? throw new ArgumentNullException(nameof(overview));
        Title = "Creation gear";
        AutomationId = "creation-gear-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow("Character creation · SR5 Resources"));
        _body.Add(NativeTheme.Title("Gear"));
        _body.Add(NativeTheme.Body(
            "Build a draft basket from the active-source Core catalog. Unsupported rows remain "
            + "visible but disabled; exact costs and legality are never inferred by this screen.",
            NativeTheme.Muted));

        CharacterCreationGearInteractionLoadResult load = _gear.Load(Coordinator.State);
        if (!string.Equals(load.Outcome, CharacterCreationGearOutcomes.Available, StringComparison.Ordinal)
            || load.State is not { } state)
        {
            AddBlockers(
                "Gear authority unavailable",
                load.Blockers.DefaultIfEmpty(load.Outcome).ToArray(),
                "creation-gear-unavailable");
            return;
        }

        InitializeBasket(state);
        AddBinding(state);
        AddPersistedBudget(state);
        if (!CreationGearPhoneAuthority.IsReady(state, Coordinator.State))
        {
            AddBlockers(
                "Gear authority blocked",
                state.Blockers.DefaultIfEmpty(CharacterCreationGearBlockers.AuthorityUnavailable).ToArray(),
                "creation-gear-blockers");
            AddAuthority(state);
            return;
        }

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
        Label binding = NativeTheme.Body(
            $"Revision {state.Binding.ContentRevision.ToString(CultureInfo.InvariantCulture)} · "
            + $"saved {state.Binding.SavedRevision.ToString(CultureInfo.InvariantCulture)} · "
            + $"snapshot {ShortDigest(state.SnapshotDigest)} · source {ShortDigest(state.Binding.SourceDigest)}",
            NativeTheme.Muted);
        binding.AutomationId = "creation-gear-binding";
        _body.Add(binding);
        _body.Add(ExactValue("creation-gear-binding-workspace-revision", state.Binding.WorkspaceRevision));
        _body.Add(ExactValue("creation-gear-binding-content-revision", state.Binding.ContentRevision));
        _body.Add(ExactValue("creation-gear-binding-saved-revision", state.Binding.SavedRevision));
        _body.Add(ExactValue("creation-gear-binding-resources-draft-revision", state.Binding.ResourcesDraftRevision));
        _body.Add(ExactValue("creation-gear-binding-raw-character-xml-digest", state.Binding.RawCharacterXmlDigest));
        _body.Add(ExactValue("creation-gear-binding-auxiliary-state-digest", state.Binding.AuxiliaryStateDigest));
        _body.Add(ExactValue("creation-gear-binding-resources-draft-digest", state.Binding.ResourcesDraftDigest));
        _body.Add(ExactValue("creation-gear-binding-snapshot-digest", state.SnapshotDigest));
    }

    private void AddPersistedBudget(CharacterCreationGearInteractionState state)
    {
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Eyebrow("Persisted exact Gear draft"));
        card.Add(NativeTheme.Metric("Starting nuyen", Nuyen(state.Budget.TotalStartingNuyen)));
        card.Add(NativeTheme.Metric("Basket cost", Nuyen(state.Budget.BasketCost)));
        card.Add(NativeTheme.Metric("Remaining", Nuyen(state.Budget.RemainingNuyen)));
        card.Add(NativeTheme.Metric("Lines", (state.PendingDraft?.Lines.Count ?? 0).ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Body(
            state.PendingDraft is null
                ? "No Gear basket has been confirmed yet."
                : $"Draft {state.PendingDraft.DraftRevision.ToString(CultureInfo.InvariantCulture)} · {ShortDigest(state.PendingDraft.DraftDigest)}",
            NativeTheme.Muted));
        card.Add(ExactValue("creation-gear-saved-basket-cost", state.Budget.BasketCost));
        card.Add(ExactValue("creation-gear-saved-remaining-nuyen", state.Budget.RemainingNuyen));
        if (state.PendingDraft is { } pending)
        {
            card.Add(ExactValue("creation-gear-saved-draft-revision", pending.DraftRevision));
            card.Add(ExactValue("creation-gear-saved-draft-digest", pending.DraftDigest));
        }
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-gear-saved-draft";
        _body.Add(border);
    }

    private void AddBasket(CharacterCreationGearInteractionState state)
    {
        _body.Add(NativeTheme.Eyebrow("Draft basket"));
        if (_basket.Count == 0)
        {
            Label empty = NativeTheme.Body("No catalog lines selected.", NativeTheme.Muted);
            empty.AutomationId = "creation-gear-basket-empty";
            _body.Add(NativeTheme.Card(empty));
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
                        "Basket authority changed",
                        [CharacterCreationGearBlockers.InvalidOption],
                        $"creation-gear-basket-invalid-{Token(optionId)}");
                    continue;
                }
                _body.Add(BasketLine(state, option, quantity));
            }
        }

        bool differs = CreationGearPhoneBasket.DiffersFromPersisted(_basket, state.PendingDraft);
        Button preview = NativeTheme.PrimaryButton("Review exact Gear basket");
        preview.AutomationId = "creation-gear-preview";
        preview.IsEnabled = differs;
        preview.Clicked += async (_, _) => await RunAsync(() => OpenPreviewAsync(state));
        _body.Add(preview);
        Label previewAuthority = NativeTheme.Body(
            differs
                ? "Core will calculate the exact basket and reject stale, illegal, unsupported, or unaffordable selections."
                : "Change the basket before requesting another preview.",
            differs ? NativeTheme.Muted : NativeTheme.Danger);
        previewAuthority.AutomationId = "creation-gear-preview-authority";
        _body.Add(previewAuthority);
    }

    private Border BasketLine(
        CharacterCreationGearInteractionState state,
        CharacterCreationGearCatalogOption option,
        int quantity)
    {
        VerticalStackLayout content = new() { Spacing = 7 };
        content.Add(NativeTheme.Title(option.Name, 18));
        content.Add(NativeTheme.Body(
            $"{option.Category} · {option.PackageCost.ToString(CultureInfo.InvariantCulture)} ¥ per "
            + $"{option.PackageQuantity.ToString(CultureInfo.InvariantCulture)} · {option.Legality}",
            NativeTheme.Muted));
        content.Add(NativeTheme.Metric("Quantity", quantity.ToString(CultureInfo.InvariantCulture)));
        content.Add(ExactValue($"creation-gear-basket-{Token(option.OptionId)}-option-id", option.OptionId));
        content.Add(ExactValue($"creation-gear-basket-{Token(option.OptionId)}-quantity", quantity));

        HorizontalStackLayout actions = new() { Spacing = 8 };
        Button decrement = NativeTheme.SecondaryButton("−");
        decrement.AutomationId = $"creation-gear-basket-{Token(option.OptionId)}-decrement";
        decrement.Clicked += (_, _) => UpdateQuantity(state, option.OptionId, quantity - 1);
        actions.Add(decrement);
        Button increment = NativeTheme.SecondaryButton("+");
        increment.AutomationId = $"creation-gear-basket-{Token(option.OptionId)}-increment";
        increment.IsEnabled = quantity < state.Authority.MaximumQuantityPerLine;
        increment.Clicked += (_, _) => UpdateQuantity(state, option.OptionId, quantity + 1);
        actions.Add(increment);
        Button remove = NativeTheme.SecondaryButton("Remove");
        remove.AutomationId = $"creation-gear-basket-{Token(option.OptionId)}-remove";
        remove.Clicked += (_, _) => UpdateQuantity(state, option.OptionId, 0);
        actions.Add(remove);
        content.Add(actions);
        Border card = NativeTheme.Card(content);
        card.AutomationId = $"creation-gear-basket-{Token(option.OptionId)}";
        return card;
    }

    private void AddCatalog(CharacterCreationGearInteractionState state)
    {
        _body.Add(NativeTheme.Eyebrow("Active-source catalog"));
        SearchBar search = new()
        {
            AutomationId = "creation-gear-search",
            Placeholder = "Search name, category, source, or legality",
            Text = _filter,
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text,
            PlaceholderColor = NativeTheme.Muted
        };
        search.SearchButtonPressed += (_, _) => ApplyFilter(search.Text);
        search.TextChanged += (_, args) =>
        {
            if (string.IsNullOrWhiteSpace(args.NewTextValue) && !string.IsNullOrWhiteSpace(_filter))
                ApplyFilter(string.Empty);
        };
        _body.Add(search);

        CharacterCreationGearCatalogOption[] matches = state.Authority.Options
            .Where(MatchesFilter)
            .OrderBy(option => option.Category, StringComparer.Ordinal)
            .ThenBy(option => option.Name, StringComparer.Ordinal)
            .ThenBy(option => option.OptionId, StringComparer.Ordinal)
            .ToArray();
        _catalogOffset = Math.Min(_catalogOffset, Math.Max(0, matches.Length - 1) / CatalogPageSize * CatalogPageSize);
        int end = Math.Min(matches.Length, _catalogOffset + CatalogPageSize);
        Label range = NativeTheme.Body(
            matches.Length == 0
                ? "No catalog rows match this search."
                : $"Showing {_catalogOffset + 1}–{end} of {matches.Length.ToString(CultureInfo.InvariantCulture)} rows.",
            NativeTheme.Muted);
        range.AutomationId = "creation-gear-catalog-range";
        _body.Add(range);

        foreach (CharacterCreationGearCatalogOption option in matches.Skip(_catalogOffset).Take(CatalogPageSize))
        {
            bool alreadySelected = _basket.ContainsKey(option.OptionId);
            bool lineRoom = alreadySelected || _basket.Count < state.Authority.MaximumBasketLines;
            bool enabled = option.IsSelectable
                           && option.PricingIsExact
                           && option.AvailabilityIsExact
                           && option.Blockers.Count == 0
                           && lineRoom
                           && (!alreadySelected
                               || _basket[option.OptionId] < state.Authority.MaximumQuantityPerLine);
            string detail = option.IsSelectable && option.Blockers.Count == 0
                ? $"{option.Category} · {option.PackageCost.ToString(CultureInfo.InvariantCulture)} ¥ / "
                  + $"{option.PackageQuantity.ToString(CultureInfo.InvariantCulture)} · Avail "
                  + $"{option.Availability.ToString(CultureInfo.InvariantCulture)} {option.Legality} · "
                  + $"{option.SourceBook} {option.Page}"
                : $"Unavailable · {option.Blockers.FirstOrDefault() ?? CharacterCreationGearBlockers.UnsupportedSemantics}";
            _body.Add(NativeTheme.NavigationRow(
                option.Name,
                detail,
                () =>
                {
                    int next = _basket.TryGetValue(option.OptionId, out int current) ? current + 1 : 1;
                    UpdateQuantity(state, option.OptionId, next);
                    return Task.CompletedTask;
                },
                enabled,
                $"creation-gear-catalog-{Token(option.OptionId)}"));
            _body.Add(ExactValue($"creation-gear-catalog-{Token(option.OptionId)}-option-id", option.OptionId));
            _body.Add(ExactValue($"creation-gear-catalog-{Token(option.OptionId)}-option-digest", option.OptionDigest));
        }

        HorizontalStackLayout pager = new() { Spacing = 10 };
        Button previous = NativeTheme.SecondaryButton("Previous");
        previous.AutomationId = "creation-gear-catalog-previous";
        previous.IsEnabled = _catalogOffset > 0;
        previous.Clicked += (_, _) =>
        {
            _catalogOffset = Math.Max(0, _catalogOffset - CatalogPageSize);
            Refresh();
        };
        pager.Add(previous);
        Button next = NativeTheme.SecondaryButton("Next");
        next.AutomationId = "creation-gear-catalog-next";
        next.IsEnabled = end < matches.Length;
        next.Clicked += (_, _) =>
        {
            _catalogOffset += CatalogPageSize;
            Refresh();
        };
        pager.Add(next);
        _body.Add(pager);
    }

    private void ApplyFilter(string? value)
    {
        _filter = value?.Trim() ?? string.Empty;
        _catalogOffset = 0;
        Refresh();
    }

    private bool MatchesFilter(CharacterCreationGearCatalogOption option)
    {
        if (string.IsNullOrWhiteSpace(_filter))
            return true;
        return option.Name.Contains(_filter, StringComparison.CurrentCultureIgnoreCase)
               || option.Category.Contains(_filter, StringComparison.CurrentCultureIgnoreCase)
               || option.SourceBook.Contains(_filter, StringComparison.CurrentCultureIgnoreCase)
               || option.Legality.Contains(_filter, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateQuantity(
        CharacterCreationGearInteractionState state,
        string optionId,
        int quantity)
    {
        if (!CreationGearPhoneBasket.TrySetQuantity(
                _basket,
                optionId,
                quantity,
                state.Authority.MaximumBasketLines,
                state.Authority.MaximumQuantityPerLine,
                out Dictionary<string, int> updated))
            return;
        _basket = updated;
        Refresh();
    }

    private async Task OpenPreviewAsync(CharacterCreationGearInteractionState state)
    {
        if (!CreationGearPhoneBasket.TryCreateSelections(
                _basket,
                state.Authority.MaximumBasketLines,
                state.Authority.MaximumQuantityPerLine,
                out CharacterCreationGearSelection[] basket))
        {
            await DisplayAlertAsync("Gear preview unavailable", CharacterCreationGearBlockers.InvalidBasket, "OK");
            return;
        }

        CharacterCreationGearInteractionPrepareResult result = _gear.Prepare(Coordinator.State, basket);
        if (result.PreparedPreview is not { } prepared
            || !string.Equals(result.Outcome, CharacterCreationGearOutcomes.Available, StringComparison.Ordinal)
            || !CreationGearPhoneAuthority.PreparedMatches(prepared, state, Coordinator.State))
        {
            string blocker = result.Blockers.FirstOrDefault()
                             ?? CharacterCreationGearInteractionBlockers.PreparedPreviewMismatch;
            await DisplayAlertAsync("Gear preview unavailable", blocker, "OK");
            Refresh();
            return;
        }
        await Navigation.PushAsync(new CreationGearPreviewPage(Coordinator, _gear, _overview, prepared));
    }

    private void AddAuthority(CharacterCreationGearInteractionState state)
    {
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Eyebrow("Core authority"));
        card.Add(NativeTheme.Metric("Profile", state.Authority.SettingsProfileId));
        card.Add(NativeTheme.Metric("Catalog rows", state.Authority.Options.Count.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric("Maximum availability", state.Authority.MaximumAvailability.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric("Maximum basket lines", state.Authority.MaximumBasketLines.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric("Maximum quantity", state.Authority.MaximumQuantityPerLine.ToString(CultureInfo.InvariantCulture)));
        card.Add(ExactValue("creation-gear-authority-digest", state.Binding.AuthorityDigest));
        card.Add(ExactValue("creation-gear-source-digest", state.Binding.SourceDigest));
        card.Add(ExactValue("creation-gear-rules-digest", state.Binding.RulesDigest));
        card.Add(ExactValue("creation-gear-runtime-digest", state.Binding.RuntimeDigest));
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-gear-authority";
        _body.Add(border);
    }

    private void AddBlockers(string title, IReadOnlyList<string> blockers, string automationId)
    {
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Eyebrow(title));
        foreach (string blocker in blockers.Distinct(StringComparer.Ordinal))
            card.Add(NativeTheme.Body(blocker, NativeTheme.Danger));
        Border border = NativeTheme.Card(card);
        border.AutomationId = automationId;
        _body.Add(border);
    }

    private static string Nuyen(decimal value) => value.ToString("N0", CultureInfo.InvariantCulture) + " ¥";
    private static string ShortDigest(string value) =>
        string.IsNullOrWhiteSpace(value) ? "unavailable" : value[..Math.Min(19, value.Length)];
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
        CharacterCreationGearPreparedPreview prepared) : base(coordinator)
    {
        _gear = gear ?? throw new ArgumentNullException(nameof(gear));
        _overview = overview ?? throw new ArgumentNullException(nameof(overview));
        _prepared = prepared ?? throw new ArgumentNullException(nameof(prepared));
        Title = "Review Gear";
        AutomationId = "creation-gear-preview-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow("Explicit review"));
        _body.Add(NativeTheme.Title("Gear basket preview"));
        AddExactPreview();

        if (_receipt is { } receipt)
        {
            AddReceipt(receipt);
            _body.Add(NativeTheme.NavigationRow(
                "Back to Gear",
                "Reopen the persisted Core Gear draft",
                () => Navigation.PopAsync(),
                automationId: "creation-gear-reopen"));
            return;
        }

        if (!string.IsNullOrWhiteSpace(_failure))
        {
            Label failure = NativeTheme.Body(_failure, NativeTheme.Danger);
            failure.AutomationId = "creation-gear-confirm-failed";
            _body.Add(NativeTheme.Card(failure));
        }

        CharacterCreationGearInteractionLoadResult load = _gear.Load(Coordinator.State);
        bool exact = load.State is { } current
                     && CreationGearPhoneAuthority.PreparedMatches(_prepared, current, Coordinator.State);
        Button confirm = NativeTheme.PrimaryButton("Confirm Gear draft");
        confirm.AutomationId = "creation-gear-confirm";
        confirm.IsEnabled = exact;
        confirm.Clicked += async (_, _) => await RunAsync(ConfirmAsync);
        _body.Add(confirm);
        Label authority = NativeTheme.Body(
            exact
                ? "This stores only the typed Gear draft; the raw character XML remains byte-identical."
                : "The workspace, Resources draft, or Gear authority changed. Reopen Gear before confirming.",
            exact ? NativeTheme.Muted : NativeTheme.Danger);
        authority.AutomationId = "creation-gear-confirm-authority";
        _body.Add(authority);
    }

    private void AddExactPreview()
    {
        VerticalStackLayout budget = new() { Spacing = 6 };
        budget.Add(NativeTheme.Eyebrow("Exact Core projection"));
        budget.Add(NativeTheme.Metric("Basket before", Nuyen(_prepared.Preview.BudgetBefore.BasketCost)));
        budget.Add(NativeTheme.Metric("Basket after", Nuyen(_prepared.Preview.BudgetAfter.BasketCost)));
        budget.Add(NativeTheme.Metric("Remaining", Nuyen(_prepared.Preview.BudgetAfter.RemainingNuyen)));
        budget.Add(NativeTheme.Metric("Lines", _prepared.Preview.After.Lines.Count.ToString(CultureInfo.InvariantCulture)));
        budget.Add(ExactValue("creation-gear-preview-basket-cost", _prepared.Preview.BudgetAfter.BasketCost));
        budget.Add(ExactValue("creation-gear-preview-remaining-nuyen", _prepared.Preview.BudgetAfter.RemainingNuyen));
        budget.Add(ExactValue("creation-gear-preview-digest", _prepared.Preview.PreviewDigest));
        budget.Add(ExactValue("creation-gear-preview-state-snapshot-digest", _prepared.StateSnapshotDigest));
        Border budgetCard = NativeTheme.Card(budget);
        budgetCard.AutomationId = "creation-gear-preview-budget";
        _body.Add(budgetCard);

        foreach (CharacterCreationGearLine line in _prepared.Preview.After.Lines)
        {
            VerticalStackLayout content = new() { Spacing = 5 };
            content.Add(NativeTheme.Title(line.Name, 18));
            content.Add(NativeTheme.Body(
                $"{line.Category} · quantity {line.Quantity.ToString(CultureInfo.InvariantCulture)} · "
                + $"{Nuyen(line.TotalCost)} · Avail {line.Availability.ToString(CultureInfo.InvariantCulture)} "
                + $"{line.Legality} · {line.SourceBook} {line.Page}",
                NativeTheme.Muted));
            content.Add(ExactValue($"creation-gear-preview-line-{Token(line.OptionId)}-option-id", line.OptionId));
            content.Add(ExactValue($"creation-gear-preview-line-{Token(line.OptionId)}-quantity", line.Quantity));
            content.Add(ExactValue($"creation-gear-preview-line-{Token(line.OptionId)}-digest", line.LineDigest));
            Border lineCard = NativeTheme.Card(content);
            lineCard.AutomationId = $"creation-gear-preview-line-{Token(line.OptionId)}";
            _body.Add(lineCard);
        }
    }

    private async Task ConfirmAsync()
    {
        _failure = null;
        CharacterCreationGearInteractionConfirmResult result = _gear.Confirm(
            Coordinator.State,
            new CharacterCreationGearConfirmation(
                _prepared,
                _prepared.Preview.PreviewDigest,
                _prepared.IdempotencyKey,
                ExplicitlyConfirmed: true));
        if (result.Receipt is not { } receipt
            || result.RefreshedState is not { } refreshed
            || result.Outcome is not (CharacterCreationGearOutcomes.Applied or CharacterCreationGearOutcomes.Replayed)
            || !CreationGearPhoneAuthority.ReceiptMatches(_prepared, receipt)
            || !CreationGearPhoneAuthority.RefreshedStateMatches(_prepared, receipt, refreshed))
        {
            _failure = result.Blockers.FirstOrDefault()
                       ?? CharacterCreationGearInteractionBlockers.ReceiptMismatch;
            return;
        }

        await _overview.LoadAsync(receipt.WorkspaceId, CancellationToken.None);
        CharacterCreationGearInteractionLoadResult reopened = _gear.Load(_overview.State);
        if (reopened.State is not { } reopenedState
            || !CreationGearPhoneAuthority.RefreshedStateMatches(_prepared, receipt, reopenedState)
            || _overview.State.ContentRevision != receipt.WorkspaceRevision
            || _overview.State.SavedRevision != receipt.SavedRevision)
        {
            _failure = CharacterCreationGearInteractionBlockers.RefreshAuthorityRequired;
            return;
        }
        _receipt = receipt;
    }

    private void AddReceipt(CharacterCreationGearReceipt receipt)
    {
        VerticalStackLayout content = new() { Spacing = 6 };
        content.Add(NativeTheme.Eyebrow("Persisted and rebound"));
        content.Add(NativeTheme.Metric("Receipt", receipt.ReceiptId));
        content.Add(NativeTheme.Metric("Workspace revision", receipt.WorkspaceRevision.ToString(CultureInfo.InvariantCulture)));
        content.Add(NativeTheme.Metric("Draft revision", receipt.DraftRevision.ToString(CultureInfo.InvariantCulture)));
        content.Add(NativeTheme.Metric("Basket cost", Nuyen(receipt.BasketCost)));
        content.Add(ExactValue("creation-gear-receipt-workspace-revision", receipt.WorkspaceRevision));
        content.Add(ExactValue("creation-gear-receipt-saved-revision", receipt.SavedRevision));
        content.Add(ExactValue("creation-gear-receipt-resources-draft-revision", receipt.ResourcesDraftRevision));
        content.Add(ExactValue("creation-gear-receipt-draft-revision", receipt.DraftRevision));
        content.Add(ExactValue("creation-gear-receipt-line-count", receipt.LineCount));
        content.Add(ExactValue("creation-gear-receipt-basket-cost", receipt.BasketCost));
        content.Add(ExactValue("creation-gear-receipt-remaining-nuyen", receipt.RemainingNuyen));
        content.Add(ExactValue("creation-gear-receipt-raw-character-xml-digest", receipt.RawCharacterXmlDigest));
        content.Add(ExactValue("creation-gear-receipt-resources-draft-digest", receipt.ResourcesDraftDigest));
        content.Add(ExactValue("creation-gear-receipt-command-digest", receipt.CommandDigest));
        content.Add(ExactValue("creation-gear-receipt-preview-digest", receipt.PreviewDigest));
        content.Add(ExactValue("creation-gear-receipt-draft-digest", receipt.DraftDigest));
        content.Add(ExactValue("creation-gear-receipt-digest", receipt.ReceiptDigest));
        Border card = NativeTheme.Card(content);
        card.AutomationId = "creation-gear-confirm-receipt";
        _body.Add(card);
    }

    private static string Nuyen(decimal value) => value.ToString("N0", CultureInfo.InvariantCulture) + " ¥";
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
        && refreshed.Budget == draft.Budget
        && !draft.CharacterEffectsApplied
        && refreshed.Budget.IsExact
        && refreshed.Blockers.Count == 0
        && CharacterCreationGearRules.IsCanonicalDigest(refreshed.SnapshotDigest);

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
        && CharacterCreationGearRules.IsCanonicalDigest(state.Binding.AuxiliaryStateDigest)
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
