using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed partial class CreationGearPage
{
    private CharacterCreationLoadoutState? _loadoutState;
    private CharacterCreationResourcesBudget? _sharedPurchaseBudget;
    private string? _loadoutSnapshot;
    private readonly Dictionary<string, int> _newLoadout = new(StringComparer.Ordinal);
    private string _loadoutKind = CharacterCreationLoadoutKinds.Weapon;
    private string _loadoutFilter = string.Empty;
    private int _loadoutOffset;
    private VerticalStackLayout _loadoutBody = new();
    private CharacterCreationLoadoutPreparedPreview? _loadoutPrepared;
    private CharacterOverviewState? _loadoutOriginal;
    private CharacterCreationLoadoutReceipt? _loadoutReceipt;
    private CharacterCreationGearInteractionState? _loadoutGearBaseline;
    private CancellationTokenSource? _loadoutRead;
    private bool _loadoutSubmitted;
    private bool _loadoutBusy;
    private bool _loadoutNeedsRefresh;
    private string? _loadoutFeedback;
    private Button? _loadoutAction;
    private ActivityIndicator? _loadoutProgress;

    private static string LoadoutCopy(string key, string fallback) => CreationFlowStrings.Get("Loadout." + key, fallback);

    private static string LoadoutFailure(IReadOnlyList<string> blockers)
        => blockers.Contains(CharacterCreationResourcesBlockers.InsufficientNuyen)
            ? LoadoutCopy("InsufficientFunds", "Not enough funds. Remove a new selection or choose something cheaper.")
            : LoadoutCopy("Changed", "These choices could not be reviewed. Reopen this runner to refresh.");

    private void AddLoadout()
    {
        _loadoutBody = new VerticalStackLayout { Spacing = 10 };
        var body = _loadoutBody;
        _loadoutAction = null;
        _loadoutProgress = null;
        long appearance = CaptureAppearanceGeneration();
        bool Current() => ReferenceEquals(body, _loadoutBody) && IsCurrentAppearanceGeneration(appearance)
            && !_loadoutBusy && (_loadoutSubmitted
                ? _loadoutOriginal is { } owner && Coordinator.CanDisplayCreationPurchase(owner)
                : _loadedDisplay is { } display && Coordinator.IsCreationCatalogDisplayCurrent(display));
        body.Add(NativeTheme.Title(LoadoutCopy("Title", "Weapons and armor"), 20));
        body.Add(NativeTheme.Body(LoadoutCopy("CatalogScope", "Only supported weapons and armor from your enabled books are offered here."), NativeTheme.Muted));
        _body.Add(NativeTheme.Card(body));
        _loadoutProgress = new ActivityIndicator { AutomationId = "creation-loadout-progress", IsVisible = false };
        body.Add(_loadoutProgress);
        if (_loadoutState is not { } state)
        {
            body.Add(NativeTheme.Body(LoadoutCopy("Unavailable", "Weapon and armor choices are unavailable. Reopen this runner to refresh."), NativeTheme.Muted));
            return;
        }
        if (_loadoutSnapshot != state.Inputs.SnapshotDigest)
        {
            _newLoadout.Clear();
            _loadoutOffset = 0;
            _loadoutSnapshot = state.Inputs.SnapshotDigest;
            if (!_loadoutSubmitted && _loadoutPrepared is { } expired)
            {
                Coordinator.CancelCreationLoadout(expired);
                _loadoutPrepared = null;
            }
        }
        if (_loadoutFeedback is not null)
        {
            var feedback = NativeTheme.Body(_loadoutFeedback, NativeTheme.Muted);
            feedback.AutomationId = "creation-loadout-feedback";
            body.Add(feedback);
        }
        foreach (var line in state.Inputs.PendingDraft?.Lines ?? [])
        {
            var retained = NativeTheme.Body($"{line.Name} × {line.Quantity} · {Nuyen(line.TotalCost)} · "
                + LoadoutCopy("Retained", "Saved — retained"), NativeTheme.Muted);
            retained.AutomationId = "creation-loadout-retained-" + Token(line.OptionId);
            body.Add(retained);
        }
        if (state.Inputs.PendingDraft is not null)
            body.Add(NativeTheme.Body(LoadoutCopy("RetainedHelp", "Saved entries will be kept when you finish this character. Only new selections can be removed here."), NativeTheme.Muted));

        if (_loadoutPrepared is { } prepared)
        {
            foreach (var line in prepared.Preview.Projection.Lines)
                body.Add(NativeTheme.Body($"{line.Name} × {line.Quantity} · {Nuyen(line.TotalCost)}"));
            var remaining = NativeTheme.Metric(_copy["Common.Remaining"], Nuyen(prepared.Preview.RemainingNuyen));
            remaining.AutomationId = "creation-loadout-preview-remaining";
            body.Add(remaining);
            var confirm = NativeTheme.PrimaryButton(_loadoutSubmitted
                ? LoadoutCopy("CheckReceipt", "Check saved purchase") : LoadoutCopy("Confirm", "Confirm weapons and armor"));
            confirm.AutomationId = _loadoutSubmitted ? "creation-loadout-recover" : "creation-loadout-confirm";
            confirm.IsEnabled = Current();
            confirm.Clicked += async (_, _) =>
            {
                if (Current() && ReferenceEquals(prepared, _loadoutPrepared))
                    await RunAsync(() => ConfirmLoadoutAsync(appearance));
            };
            body.Add(confirm);
            _loadoutAction = confirm;
            if (!_loadoutSubmitted)
            {
                var cancel = NativeTheme.SecondaryButton(LoadoutCopy("CancelPreview", "Back to selections"));
                cancel.AutomationId = "creation-loadout-cancel";
                cancel.Clicked += (_, _) =>
                {
                    if (!Current() || !ReferenceEquals(prepared, _loadoutPrepared)) return;
                    Coordinator.CancelCreationLoadout(prepared);
                    _loadoutPrepared = null;
                    Refresh();
                };
                body.Add(cancel);
            }
            return;
        }

        var authority = state.Inputs.Authority;
        var options = authority.Options.Where(option => option.IsSelectable && option.PricingIsExact
            && option.AvailabilityIsExact && option.Blockers.Count == 0).ToArray();
        foreach (var selection in _newLoadout.OrderBy(row => row.Key, StringComparer.Ordinal))
        {
            var option = options.Single(row => row.OptionId == selection.Key);
            var line = new HorizontalStackLayout { Spacing = 8 };
            line.Add(NativeTheme.Body($"{option.Name} × {selection.Value}"));
            var remove = NativeTheme.SecondaryButton(_copy["Gear.Remove"]);
            remove.AutomationId = "creation-loadout-remove-" + Token(option.OptionId);
            remove.Clicked += (_, _) => { if (Current()) { _newLoadout.Remove(option.OptionId); Refresh(); } };
            line.Add(remove);
            body.Add(line);
        }
        var preview = NativeTheme.PrimaryButton(LoadoutCopy("Preview", "Preview weapons and armor"));
        preview.AutomationId = "creation-loadout-preview";
        preview.IsEnabled = _newLoadout.Count > 0;
        preview.Clicked += async (_, _) => { if (Current()) await RunAsync(() => PreviewLoadoutAsync(state, appearance)); };
        body.Add(preview);
        _loadoutAction = preview;

        var type = new Picker
        {
            AutomationId = "creation-loadout-type", Title = CreationFlowStrings.Get("Gear.Filter.Type", "Equipment type"),
            ItemsSource = new[] { LoadoutCopy("Weapons", "Weapons"), LoadoutCopy("Armor", "Armor") },
            SelectedIndex = _loadoutKind == CharacterCreationLoadoutKinds.Weapon ? 0 : 1,
            TextColor = NativeTheme.Text, BackgroundColor = NativeTheme.Surface
        };
        type.SelectedIndexChanged += (_, _) =>
        {
            if (!Current() || type.SelectedIndex < 0) return;
            _loadoutKind = type.SelectedIndex == 0 ? CharacterCreationLoadoutKinds.Weapon : CharacterCreationLoadoutKinds.Armor;
            _loadoutOffset = 0;
            Refresh();
        };
        body.Add(type);
        var search = new SearchBar { AutomationId = "creation-loadout-search", Placeholder = _copy["Gear.Search"],
            Text = _loadoutFilter, TextColor = NativeTheme.Text, BackgroundColor = NativeTheme.Surface };
        search.SearchButtonPressed += (_, _) =>
        {
            if (!Current()) return;
            _loadoutFilter = search.Text?.Trim() ?? string.Empty;
            _loadoutOffset = 0;
            search.Unfocus();
            Refresh();
        };
        search.TextChanged += (_, args) =>
        {
            if (Current() && string.IsNullOrWhiteSpace(args.NewTextValue) && _loadoutFilter.Length > 0)
            { _loadoutFilter = string.Empty; _loadoutOffset = 0; Refresh(); }
        };
        body.Add(search);
        var retainedIds = state.Inputs.OriginalSelections.Weapons.Select(row => row.OptionId)
            .Concat(state.Inputs.OriginalSelections.Armor.Select(row => row.OptionId)).ToHashSet(StringComparer.Ordinal);
        // The current typed Core lane admits one armor, quantity one. Do not
        // offer additional rows that are guaranteed to fail its preview.
        bool armorSelected = state.Inputs.OriginalSelections.Armor.Count > 0
            || options.Any(row => row.Kind == CharacterCreationLoadoutKinds.Armor && _newLoadout.ContainsKey(row.OptionId));
        var available = options.Where(row => row.Kind == _loadoutKind && !retainedIds.Contains(row.OptionId)
            && (row.Kind != CharacterCreationLoadoutKinds.Armor || !armorSelected)
            && (row.Name.Contains(_loadoutFilter, StringComparison.OrdinalIgnoreCase)
                || row.Category.Contains(_loadoutFilter, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(row => row.OptionId, StringComparer.Ordinal).ToArray();
        foreach (var option in available.Skip(_loadoutOffset).Take(CatalogPageSize))
        {
            var add = NativeTheme.SecondaryButton($"{option.Name} · {Nuyen(option.UnitCost)} +");
            add.AutomationId = "creation-loadout-add-" + Token(option.OptionId);
            int quantity = _newLoadout.GetValueOrDefault(option.OptionId);
            add.IsEnabled = quantity < authority.MaximumQuantityPerLine
                && (quantity > 0 || _newLoadout.Count + retainedIds.Count < authority.MaximumBasketLines);
            add.Clicked += (_, _) =>
            {
                if (!Current() || !add.IsEnabled) return;
                _newLoadout[option.OptionId] = quantity + 1;
                Refresh();
            };
            body.Add(add);
        }
        var pages = new HorizontalStackLayout { Spacing = 8 };
        var previous = NativeTheme.SecondaryButton(_copy["Common.Previous"]);
        previous.AutomationId = "creation-loadout-previous";
        previous.IsEnabled = _loadoutOffset > 0;
        previous.Clicked += (_, _) => { if (Current() && previous.IsEnabled) { _loadoutOffset -= CatalogPageSize; Refresh(); } };
        pages.Add(previous);
        var next = NativeTheme.SecondaryButton(_copy["Common.Next"]);
        next.AutomationId = "creation-loadout-next";
        next.IsEnabled = _loadoutOffset + CatalogPageSize < available.Length;
        next.Clicked += (_, _) => { if (Current() && next.IsEnabled) { _loadoutOffset += CatalogPageSize; Refresh(); } };
        pages.Add(next);
        body.Add(pages);
    }

    private async Task PreviewLoadoutAsync(CharacterCreationLoadoutState state, long appearance)
    {
        if (_loadoutBusy || _loadedDisplay is not { } display || !Coordinator.IsCreationCatalogDisplayCurrent(display)) return;
        var weapons = state.Inputs.OriginalSelections.Weapons.ToList();
        var armor = state.Inputs.OriginalSelections.Armor.ToList();
        foreach (var row in _newLoadout)
        {
            var option = state.Inputs.Authority.Options.Single(option => option.OptionId == row.Key);
            if (option.Kind == CharacterCreationLoadoutKinds.Weapon) weapons.Add(new(row.Key, row.Value));
            else armor.Add(new(row.Key, row.Value));
        }
        using var read = new CancellationTokenSource();
        _loadoutRead = read;
        _loadoutBusy = true;
        ShowLoadoutProgress();
        try
        {
            var result = await Coordinator.PrepareCreationLoadoutForDisplayAsync(display,
                new(weapons.OrderBy(row => row.OptionId, StringComparer.Ordinal).ToArray(),
                    armor.OrderBy(row => row.OptionId, StringComparer.Ordinal).ToArray()), read.Token);
            if (!IsCurrentAppearanceGeneration(appearance) || !Coordinator.IsCreationCatalogDisplayCurrent(display))
            { if (result.Prepared is { } departed) Coordinator.CancelCreationLoadout(departed); return; }
            if (result.State?.Inputs.SnapshotDigest != state.Inputs.SnapshotDigest
                || result.State.Resources.SnapshotDigest != state.Resources.SnapshotDigest)
            {
                if (result.Prepared is { } stale) Coordinator.CancelCreationLoadout(stale);
                _loadoutFeedback = LoadoutCopy("Changed", "These choices could not be reviewed. Reopen this runner to refresh.");
                return;
            }
            _loadoutPrepared = result.Prepared;
            _loadoutOriginal = display;
            _loadoutSubmitted = false;
            _loadoutFeedback = result.Prepared is null ? LoadoutFailure(result.Blockers) : null;
        }
        catch (OperationCanceledException) when (read.IsCancellationRequested) { }
        finally
        {
            _loadoutRead = null;
            _loadoutBusy = false;
            await RefreshLoadoutAfterReadAsync(appearance, display);
        }
    }

    private async Task ConfirmLoadoutAsync(long appearance)
    {
        if (_loadoutBusy || _loadoutPrepared is not { } prepared || _loadoutOriginal is not { } display) return;
        bool lookup = _loadoutSubmitted;
        // Retain the pre-dispatch basket authority until the exact committed
        // purchase is observed. A partially successful reread is not a baseline.
        if (!lookup) _loadoutGearBaseline = _loaded?.State;
        _loadoutSubmitted = true;
        _loadoutBusy = true;
        ShowLoadoutProgress();
        using var read = new CancellationTokenSource();
        _loadoutRead = read;
        try
        {
            var purchase = await Coordinator.ConfirmCreationLoadoutPurchaseAsync(display, prepared, lookup, read.Token);
            if (purchase.Result.Receipt is { } receipt)
            {
                _loadoutReceipt = receipt;
                _loadoutNeedsRefresh = true;
                _loadoutPrepared = null;
                _loadoutSubmitted = false;
                _newLoadout.Clear();
                _loadoutFeedback = purchase.Display is null
                    ? LoadoutCopy("SavedReopen", "Saved. Reopen the character to refresh both baskets.")
                    : LoadoutCopy("Saved", "Weapons and armor saved. You can continue choosing equipment.");
                if (purchase.Display is { } current && purchase.Gear is { } gear && purchase.Loadout is { } loadout
                    && IsCurrentAppearanceGeneration(appearance) && Coordinator.IsCreationCatalogDisplayCurrent(current))
                {
                    PreserveStagedGearForPurchaseRefresh(_loadoutGearBaseline, gear.State, loadout, receipt);
                    _loadoutGearBaseline = null;
                    _loadedDisplay = current;
                    _loaded = gear;
                    _loadoutState = loadout;
                    _sharedPurchaseBudget = purchase.Budget;
                    _ready = true;
                    _loadoutNeedsRefresh = false;
                }
            }
            else if (purchase.Result.Outcome == CharacterCreationLoadoutInteractionPresenter.OutcomeUnknown)
                _loadoutFeedback = LoadoutCopy("Unknown", "The purchase is not verified. Check the saved purchase before trying again.");
            else
            {
                _loadoutPrepared = null;
                _loadoutSubmitted = false;
                _loadoutGearBaseline = null;
                _loadoutFeedback = LoadoutFailure(purchase.Result.Blockers);
            }
        }
        catch (OperationCanceledException) when (read.IsCancellationRequested) { }
        finally
        {
            _loadoutRead = null;
            _loadoutBusy = false;
            await RefreshLoadoutAfterReadAsync(appearance, display);
        }
    }

    private Task RefreshLoadoutAfterReadAsync(long appearance, CharacterOverviewState original)
    {
        if (IsCurrentAppearanceGeneration(appearance))
        {
            Refresh();
            return Task.CompletedTask;
        }
        // The old result cannot populate a later appearance. It can request a
        // current, owner-checked render of the pending/saved recovery state.
        // The native action gate drains this request after the old action exits.
        return Coordinator.CanDisplayCreationPurchase(original)
            ? RefreshAfterBackgroundReadAsync(CaptureAppearanceGeneration()) : Task.CompletedTask;
    }

    private void PreserveStagedGearForPurchaseRefresh(
        CharacterCreationGearInteractionState? previous, CharacterCreationGearInteractionState? current,
        CharacterCreationLoadoutState? loadout, CharacterCreationLoadoutReceipt? receipt)
    {
        if (CanPreserveStagedGearForPurchaseRefresh(_initializedSnapshotDigest, previous, current, loadout, receipt))
            _initializedSnapshotDigest = current!.SnapshotDigest;
    }

    private static bool CanPreserveStagedGearForPurchaseRefresh(string? initializedSnapshotDigest,
        CharacterCreationGearInteractionState? previous, CharacterCreationGearInteractionState? current,
        CharacterCreationLoadoutState? loadout, CharacterCreationLoadoutReceipt? receipt)
    {
        // A weapon/armor checkpoint changes the workspace snapshot, not the
        // player's unconfirmed Gear basket. Carry it only across the same
        // admitted source/funding, exact purchase revision and unchanged persisted Gear. AddBasket and
        // the next Core preview evaluate it against the newly reduced wallet.
        if (previous is null || current is null || loadout is null || receipt is null
            || previous.Binding.WorkspaceId != receipt.WorkspaceId
            || previous.Binding.ContentRevision != receipt.PreviousContentRevision
            || previous.Binding.SavedRevision != receipt.PreviousSavedRevision
            || current.Binding.ContentRevision != receipt.ContentRevision
            || current.Binding.SavedRevision != receipt.SavedRevision
            || loadout.Inputs.Binding.WorkspaceId != receipt.WorkspaceId
            || loadout.Inputs.Binding.ContentRevision != receipt.ContentRevision
            || loadout.Inputs.Binding.SavedRevision != receipt.SavedRevision
            || loadout.Inputs.PendingDraft?.DraftDigest != receipt.Draft.DraftDigest
            || loadout.Inputs.Receipts?.LastOrDefault()?.ReceiptDigest != receipt.ReceiptDigest
            || !CharacterCreationGearRules.DigestsEqual(initializedSnapshotDigest, previous.SnapshotDigest)
            || previous.Binding.WorkspaceId != current.Binding.WorkspaceId
            || previous.PendingDraft?.DraftDigest != current.PendingDraft?.DraftDigest
            || previous.Binding.RawCharacterXmlDigest != current.Binding.RawCharacterXmlDigest
            || previous.Binding.ResourcesDraftDigest != current.Binding.ResourcesDraftDigest
            || previous.Binding.AuthorityDigest != current.Binding.AuthorityDigest
            || previous.Binding.SourceDigest != current.Binding.SourceDigest
            || previous.Binding.RulesDigest != current.Binding.RulesDigest
            || previous.Binding.RuntimeDigest != current.Binding.RuntimeDigest)
            return false;
        return true;
    }

    private void ShowLoadoutProgress()
    {
        if (_loadoutAction is { } action) action.IsEnabled = false;
        if (_loadoutProgress is { } progress) progress.IsRunning = progress.IsVisible = true;
    }
}
