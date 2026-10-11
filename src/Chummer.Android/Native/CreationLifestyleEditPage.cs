using System.Globalization;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

/// <summary>Configures one typed Lifestyle draft without touching workspace XML.</summary>
public sealed class CreationLifestyleEditPage : NativePageBase
{
    private readonly Guid _lifestyleId;
    private readonly string? _optionId;
    private readonly bool _isCreate;
    private readonly CreationLifestylesPhoneDraft _draft = new();
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };
    private Button? _previewButton;
    private IReadOnlyList<string> _prepareBlockers = [];
    private CharacterCreationLifestylesInteractionLoadResult? _loaded;
    private CharacterOverviewState? _display;
    private bool _loading = true;
    private bool _admitted;
    private bool _preparing;
    private bool _qualitiesExpanded;
    private long _render;
    private CancellationTokenSource? _previewLifetime;
    private VerticalStackLayout _technicalDetails = new() { Spacing = 6 };

    internal CreationLifestyleEditPage(
        RunnerSessionCoordinator coordinator,
        Guid lifestyleId,
        string? optionId,
        bool isCreate) : base(coordinator)
    {
        if (lifestyleId == Guid.Empty)
            throw new ArgumentException("A stable Lifestyle identity is required.", nameof(lifestyleId));
        if (isCreate && string.IsNullOrWhiteSpace(optionId))
            throw new ArgumentException("A stable Core catalog option is required.", nameof(optionId));
        _lifestyleId = lifestyleId;
        _optionId = optionId;
        _isCreate = isCreate;
        Title = isCreate ? "Add lifestyle" : "Edit lifestyle";
        AutomationId = "creation-lifestyle-edit-page";
        Content = new ScrollView { Content = _body };
    }

    protected override async Task PrepareForAppearanceRefreshAsync(CancellationToken cancellationToken)
    {
        _loading = true;
        _loaded = null;
        _display = null;
        _admitted = false;
        Refresh();
        try
        {
            var display = Coordinator.State;
            var loaded = await Coordinator.LoadCreationLifestylesForDisplayAsync(display, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!Coordinator.IsCreationCatalogDisplayCurrent(display)) return;
            _loaded = loaded;
            _display = display;
            _admitted = loaded.Outcome == CharacterCreationLifestyleOutcomes.Available
                && loaded.Blockers.Count == 0 && loaded.State is { } state
                && CreationLifestylesPhoneAuthority.IsReady(state, display);
        }
        finally { if (!cancellationToken.IsCancellationRequested) _loading = false; }
    }

    protected override void OnDisappearing()
    {
        _previewLifetime?.Cancel();
        base.OnDisappearing();
    }

    private bool Current(CharacterCreationLifestylesInteractionState state)
        => _admitted && ReferenceEquals(_loaded?.State, state) && _display is { } display
           && Coordinator.IsCreationCatalogDisplayCurrent(display);

    private bool CanEdit(CharacterCreationLifestylesInteractionState state, long render)
        => !_preparing && render == _render && Current(state)
           && IsCurrentAppearanceGeneration(CaptureAppearanceGeneration());

    protected override void Refresh()
    {
        _render++;
        _body.Clear();
        _technicalDetails = new() { Spacing = 6 };
        _previewButton = null;
        _body.Add(NativeTheme.Eyebrow("Character creation · Lifestyle"));
        if (_loading)
        {
            _body.Add(new ActivityIndicator { IsRunning = true, AutomationId = "creation-lifestyle-edit-loading" });
            return;
        }
        var load = _loaded;
        if (load is null || !string.Equals(load.Outcome, CharacterCreationLifestyleOutcomes.Available, StringComparison.Ordinal)
            || load.State is not { } state
            || !Current(state))
        {
            AddBlockers("Lifestyle authority unavailable", load?.Blockers ??
                [CharacterCreationLifestylesBlockers.AuthorityUnavailable], "creation-lifestyle-edit-blockers");
            return;
        }

        bool bound;
        if (_isCreate)
        {
            bound = _draft.BindCreate(state, _lifestyleId, _optionId!);
        }
        else if (CreationLifestylesPhoneAuthority.ResolveUniqueLifestyle(state, _lifestyleId) is { } existing)
        {
            bound = _draft.BindEdit(state, existing);
        }
        else
        {
            AddBlockers(
                "Lifestyle no longer exists",
                [CharacterCreationLifestylesBlockers.LifestyleNotFound],
                "creation-lifestyle-not-found");
            return;
        }
        _ = bound;

        if (_draft.Current(state) is not { } configuration)
        {
            AddBlockers(
                "Lifestyle draft could not bind",
                [CharacterCreationLifestylesBlockers.StaleWorkspaceRevision],
                "creation-lifestyle-draft-stale");
            return;
        }

        _body.Add(NativeTheme.Title(_isCreate ? "Add Lifestyle" : configuration.Name));
        AddBinding(state, configuration);
        AddBaseLifestyle(state, configuration);
        AddIdentityAndTiming(state, configuration);
        AddEconomicsInputs(state, configuration);
        AddLocation(state, configuration);
        if (_prepareBlockers.Count > 0)
        {
            AddBlockers("Preview blockers", _prepareBlockers, "creation-lifestyle-preview-blockers");
        }
        AddPreviewActions(state);
        AddQualities(state, configuration);
        long render = _render;
        _body.Add(NativeTheme.TechnicalDetails(_technicalDetails, "creation-lifestyle-edit-details",
            () => CanEdit(state, render)));
    }

    private void AddBinding(
        CharacterCreationLifestylesInteractionState state,
        CharacterCreationLifestyleConfiguration configuration)
    {
        Label binding = NativeTheme.Body(
            $"Revision {state.Binding.ContentRevision} · snapshot {ShortDigest(state.SnapshotDigest)} · "
            + $"Lifestyle {configuration.LifestyleId:D}",
            NativeTheme.Muted);
        binding.AutomationId = "creation-lifestyle-edit-binding";
        _technicalDetails.Add(binding);
    }

    private void AddBaseLifestyle(
        CharacterCreationLifestylesInteractionState state,
        CharacterCreationLifestyleConfiguration configuration)
    {
        long render = _render;
        CharacterCreationLifestyleCatalogOption[] options = state.Authority.LifestyleOptions
            .Where(option => option.IsSelectable && option.EligibilityIsExact && option.Blockers.Count == 0)
            .OrderBy(option => option.BaseCost)
            .ThenBy(option => option.Name, StringComparer.Ordinal)
            .ToArray();
        Picker picker = new()
        {
            AutomationId = "creation-lifestyle-base-option",
            Title = "Base Lifestyle"
        };
        foreach (CharacterCreationLifestyleCatalogOption option in options)
            picker.Items.Add($"{option.Name} · {option.BaseCost.ToString(CultureInfo.InvariantCulture)}");
        picker.SelectedIndex = Array.FindIndex(options, option => string.Equals(
            option.OptionId,
            configuration.BaseLifestyleOptionId,
            StringComparison.Ordinal));
        picker.SelectedIndexChanged += (_, _) =>
        {
            if (CanEdit(state, render) && picker.SelectedIndex >= 0 && picker.SelectedIndex < options.Length)
            {
                _prepareBlockers = [];
                _draft.TrySelectBase(state, options[picker.SelectedIndex].OptionId);
                Refresh();
            }
        };
        VerticalStackLayout card = new() { Spacing = 7 };
        card.Add(NativeTheme.FieldLabel("Base Lifestyle"));
        card.Add(picker);
        card.Add(NativeTheme.Body(
            "Names, prices, increments and eligibility come from the active Core catalog.",
            NativeTheme.Muted));
        _body.Add(NativeTheme.Card(card));
    }

    private void AddIdentityAndTiming(
        CharacterCreationLifestylesInteractionState state,
        CharacterCreationLifestyleConfiguration configuration)
    {
        long render = _render;
        VerticalStackLayout card = new() { Spacing = 8 };
        card.Add(NativeTheme.Eyebrow("Identity and term"));
        Entry name = NativeTheme.TextField("creation-lifestyle-name", configuration.Name);
        name.TextChanged += (_, args) =>
        {
            if (CanEdit(state, render)) Update(_draft.TrySetName(state, args.NewTextValue), state);
        };
        card.Add(NativeTheme.FieldLabel("Name"));
        card.Add(name);

        Picker style = PickerFor(
            "creation-lifestyle-style",
            CharacterCreationLifestyleStyleIds.All,
            configuration.StyleId);
        style.SelectedIndexChanged += (_, _) =>
        {
            if (CanEdit(state, render) && style.SelectedIndex >= 0)
            {
                _draft.TrySetStyle(state, CharacterCreationLifestyleStyleIds.All[style.SelectedIndex]);
                Refresh();
            }
        };
        card.Add(NativeTheme.FieldLabel("Style"));
        card.Add(style);
        card.Add(NativeTheme.Metric("Increment", configuration.IncrementId));
        card.Add(NumberEntry(
            "Increments",
            "creation-lifestyle-increments",
            configuration.Increments.ToString(CultureInfo.InvariantCulture),
            value => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                && _draft.TrySetIncrements(state, parsed),
            state));
        card.Add(NumberEntry(
            "Percentage",
            "creation-lifestyle-percentage",
            configuration.Percentage.ToString(CultureInfo.InvariantCulture),
            value => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsed)
                && _draft.TrySetPercentage(state, parsed),
            state));
        _body.Add(NativeTheme.Card(card));
    }

    private void AddEconomicsInputs(
        CharacterCreationLifestylesInteractionState state,
        CharacterCreationLifestyleConfiguration configuration)
    {
        VerticalStackLayout card = new() { Spacing = 8 };
        card.Add(NativeTheme.Eyebrow("Household and advanced values"));
        card.Add(NumberEntry(
            "Roommates",
            "creation-lifestyle-roommates",
            configuration.Roommates.ToString(CultureInfo.InvariantCulture),
            value => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                && _draft.TrySetRoommates(state, parsed),
            state));
        card.Add(Toggle(
            "Split cost with roommates",
            "creation-lifestyle-split-cost",
            configuration.SplitCostWithRoommates,
            value => _draft.TrySetSplit(state, value),
            state));
        card.Add(Toggle(
            "Trust Fund",
            "creation-lifestyle-trust-fund",
            configuration.TrustFund,
            value => _draft.TrySetTrustFund(state, value),
            state));

        bool advanced = !string.Equals(
            configuration.StyleId,
            CharacterCreationLifestyleStyleIds.Standard,
            StringComparison.Ordinal);
        if (advanced)
        {
            card.Add(NumberEntry("Area", "creation-lifestyle-area", configuration.Area.ToString(CultureInfo.InvariantCulture),
                value => int.TryParse(value, out int parsed) && _draft.TrySetArea(state, parsed), state));
            card.Add(NumberEntry("Comforts", "creation-lifestyle-comforts", configuration.Comforts.ToString(CultureInfo.InvariantCulture),
                value => int.TryParse(value, out int parsed) && _draft.TrySetComforts(state, parsed), state));
            card.Add(NumberEntry("Security", "creation-lifestyle-security", configuration.Security.ToString(CultureInfo.InvariantCulture),
                value => int.TryParse(value, out int parsed) && _draft.TrySetSecurity(state, parsed), state));
            card.Add(NumberEntry("Bonus Lifestyle Points", "creation-lifestyle-bonus-lp", configuration.BonusLifestylePoints.ToString(CultureInfo.InvariantCulture),
                value => int.TryParse(value, out int parsed) && _draft.TrySetBonusLifestylePoints(state, parsed), state));
        }
        _body.Add(NativeTheme.Card(card));
    }

    private void AddLocation(
        CharacterCreationLifestylesInteractionState state,
        CharacterCreationLifestyleConfiguration configuration)
    {
        VerticalStackLayout card = new() { Spacing = 8 };
        card.Add(NativeTheme.Eyebrow("Location"));
        AddText(card, "City", "creation-lifestyle-city", configuration.City,
            value => _draft.TrySetCity(state, value), state);
        AddText(card, "District", "creation-lifestyle-district", configuration.District,
            value => _draft.TrySetDistrict(state, value), state);
        AddText(card, "Borough", "creation-lifestyle-borough", configuration.Borough,
            value => _draft.TrySetBorough(state, value), state);
        _body.Add(NativeTheme.Card(card));
    }

    private void AddQualities(
        CharacterCreationLifestylesInteractionState state,
        CharacterCreationLifestyleConfiguration configuration)
    {
        long render = _render;
        VerticalStackLayout choices = new() { Spacing = 10, IsVisible = _qualitiesExpanded && !_preparing };
        bool populated = false;
        string Copy() => CreationFlowStrings.Get(
            choices.IsVisible ? "Lifestyles.HideQualities" : "Lifestyles.ShowQualities",
            choices.IsVisible ? "Hide optional lifestyle qualities" : "Optional lifestyle qualities");
        Button toggle = NativeTheme.ReadingButton(Copy());
        toggle.AutomationId = "creation-lifestyle-qualities-toggle";
        toggle.IsEnabled = !_preparing;
        toggle.Clicked += (_, _) =>
        {
            if (!ReferenceEquals(toggle.Parent, _body) || !CanEdit(state, render)) return;
            _qualitiesExpanded = !choices.IsVisible;
            choices.IsVisible = _qualitiesExpanded;
            if (choices.IsVisible && !populated)
            {
                PopulateQualities(choices, state, configuration, render);
                populated = true;
            }
            toggle.Text = Copy();
        };
        _body.Add(toggle);
        _body.Add(choices);
        if (choices.IsVisible)
        {
            PopulateQualities(choices, state, configuration, render);
            populated = true;
        }
    }

    private void PopulateQualities(
        VerticalStackLayout choices,
        CharacterCreationLifestylesInteractionState state,
        CharacterCreationLifestyleConfiguration configuration,
        long render)
    {
        foreach (CharacterCreationLifestyleQualityCatalogOption option in state.Authority.QualityOptions
                     .OrderBy(item => item.Category, StringComparer.Ordinal)
                     .ThenBy(item => item.Name, StringComparer.Ordinal))
        {
            bool enabled = option.IsSelectable && option.EligibilityIsExact && option.Blockers.Count == 0;
            bool selected = configuration.Qualities.Any(item =>
                !item.IsBuiltIn && string.Equals(item.OptionId, option.OptionId, StringComparison.Ordinal));
            CheckBox check = new()
            {
                AutomationId = $"creation-lifestyle-quality-{Token(option.OptionId)}",
                IsChecked = selected,
                IsEnabled = enabled,
                Color = NativeTheme.Signal
            };
            Label label = NativeTheme.Body(
                $"{option.Name} · {option.Category} · LP {option.LifestylePointCost} · "
                + $"{option.SourceBook} {option.Page}",
                enabled ? NativeTheme.Text : NativeTheme.Danger);
            Grid row = new()
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Star)
                },
                ColumnSpacing = 10
            };
            row.Add(check);
            row.Add(label, 1);
            check.CheckedChanged += (_, args) =>
            {
                if (!CanEdit(state, render)) return;
                _prepareBlockers = [];
                _draft.TryToggleQuality(state, option.OptionId, args.Value);
                UpdatePreviewEnabled(state);
            };
            Border border = NativeTheme.Card(row, new Thickness(14));
            border.AutomationId = $"creation-lifestyle-quality-card-{Token(option.OptionId)}";
            choices.Add(border);
        }
    }

    private void AddPreviewActions(CharacterCreationLifestylesInteractionState state)
    {
        long render = _render;
        _previewButton = NativeTheme.PrimaryButton(CreationFlowStrings.Get("Contacts.Review", "Review changes"));
        _previewButton.AutomationId = "creation-lifestyle-preview";
        _previewButton.IsEnabled = !_preparing && _draft.HasChanges(state);
        _previewButton.Clicked += async (_, _) =>
        {
            if (CanEdit(state, render)) await PreparePreviewAsync(state, delete: false);
        };
        _body.Add(_previewButton);

        if (!_isCreate)
        {
            Button delete = NativeTheme.SecondaryButton("Review Lifestyle deletion");
            delete.AutomationId = "creation-lifestyle-delete-preview";
            delete.IsEnabled = !_preparing;
            delete.Clicked += async (_, _) =>
            {
                if (CanEdit(state, render)) await PreparePreviewAsync(state, delete: true);
            };
            _body.Add(delete);
        }

        Label scope = NativeTheme.Body(
            "The draft contains typed option identities only. Core recalculates the exact nuyen/LP result, "
            + "proves unknown sibling and nested state preservation, and requires a separate confirmation.",
            NativeTheme.Muted);
        scope.AutomationId = "creation-lifestyle-draft-scope";
        _technicalDetails.Add(scope);
        if (_preparing)
            _body.Add(new ActivityIndicator { IsRunning = true, AutomationId = "creation-lifestyle-preview-loading" });
    }

    private Task PreparePreviewAsync(
        CharacterCreationLifestylesInteractionState state,
        bool delete)
        => RunAsync(async () =>
    {
        if (!Current(state) || _display is not { } original || _preparing) return;
        long appearance = CaptureAppearanceGeneration();
        var navigation = Navigation;
        var stack = navigation.NavigationStack.ToArray();
        if (stack.LastOrDefault() != this) return;
        _prepareBlockers = [];
        CharacterCreationLifestyleMutationInput? input = delete
            ? _draft.ToDeleteInput(state)
            : _draft.ToInput(state);
        if (input is null)
        {
            _prepareBlockers = [CharacterCreationLifestylesBlockers.NoChange];
            Refresh();
            return;
        }
        using var lifetime = new CancellationTokenSource();
        _previewLifetime = lifetime;
        _preparing = true;
        Refresh();
        try
        {
            CharacterCreationLifestylesInteractionPrepareResult result =
                await Coordinator.PrepareCreationLifestyleForDisplayAsync(original, input, lifetime.Token);
            lifetime.Token.ThrowIfCancellationRequested();
            if (!IsCurrentAppearanceGeneration(appearance) || !Current(state)
                || !navigation.NavigationStack.SequenceEqual(stack)) return;
            if (!string.Equals(result.Outcome, CharacterCreationLifestyleOutcomes.Available, StringComparison.Ordinal)
                || result.State is not { } preparedState
                || result.PreparedPreview is not { } prepared
                || result.Blockers.Count > 0
                || !CreationLifestylesPhoneAuthority.PreparedMatches(prepared, preparedState, Coordinator.State))
            {
                _prepareBlockers = result.Blockers.Count > 0
                    ? result.Blockers
                    : [CharacterCreationLifestylesBlockers.AuthorityUnavailable];
                Refresh();
                return;
            }
            await navigation.PushAsync(new CreationLifestylePreviewPage(Coordinator, prepared));
        }
        finally
        {
            if (ReferenceEquals(_previewLifetime, lifetime)) _previewLifetime = null;
            _preparing = false;
        }
    });

    private View NumberEntry(
        string label,
        string automationId,
        string value,
        Func<string, bool> apply,
        CharacterCreationLifestylesInteractionState state)
    {
        long render = _render;
        VerticalStackLayout container = new() { Spacing = 5 };
        container.Add(NativeTheme.FieldLabel(label));
        Entry entry = NativeTheme.TextField(automationId, value);
        entry.Keyboard = Keyboard.Numeric;
        entry.TextChanged += (_, args) =>
        {
            if (CanEdit(state, render)) Update(apply(args.NewTextValue ?? string.Empty), state);
        };
        container.Add(entry);
        return container;
    }

    private View Toggle(
        string label,
        string automationId,
        bool value,
        Func<bool, bool> apply,
        CharacterCreationLifestylesInteractionState state)
    {
        long render = _render;
        Switch toggle = new()
        {
            AutomationId = automationId,
            IsToggled = value,
            HorizontalOptions = LayoutOptions.Start
        };
        toggle.Toggled += (_, args) =>
        {
            if (CanEdit(state, render)) Update(apply(args.Value), state);
        };
        VerticalStackLayout container = new() { Spacing = 5 };
        container.Add(NativeTheme.FieldLabel(label));
        container.Add(toggle);
        return container;
    }

    private void AddText(
        VerticalStackLayout card,
        string label,
        string automationId,
        string value,
        Func<string?, bool> apply,
        CharacterCreationLifestylesInteractionState state)
    {
        long render = _render;
        card.Add(NativeTheme.FieldLabel(label));
        Entry entry = NativeTheme.TextField(automationId, value);
        entry.TextChanged += (_, args) =>
        {
            if (CanEdit(state, render)) Update(apply(args.NewTextValue), state);
        };
        card.Add(entry);
    }

    private static Picker PickerFor(
        string automationId,
        IReadOnlyList<string> values,
        string selected)
    {
        Picker picker = new() { AutomationId = automationId };
        foreach (string value in values)
            picker.Items.Add(RunnerSessionCoordinator.HumanizeId(value));
        picker.SelectedIndex = values.ToList().FindIndex(value => string.Equals(value, selected, StringComparison.Ordinal));
        return picker;
    }

    private void Update(bool accepted, CharacterCreationLifestylesInteractionState state)
    {
        _prepareBlockers = accepted ? [] : [CharacterCreationLifestylesBlockers.InvalidMutation];
        UpdatePreviewEnabled(state);
    }

    private void UpdatePreviewEnabled(CharacterCreationLifestylesInteractionState state)
    {
        if (_previewButton is not null)
            _previewButton.IsEnabled = !_preparing && Current(state) && _draft.HasChanges(state);
    }

    private void AddBlockers(string title, IReadOnlyList<string> blockers, string automationId)
    {
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Eyebrow(title));
        foreach (string blocker in blockers.DefaultIfEmpty(
                     CharacterCreationLifestylesBlockers.AuthorityUnavailable).Distinct(StringComparer.Ordinal))
        {
            card.Add(NativeTheme.Body(blocker, NativeTheme.Danger));
        }
        Border border = NativeTheme.Card(card);
        border.AutomationId = automationId;
        _body.Add(border);
    }

    private static string Token(string value)
        => new string(value.Select(character => char.IsLetterOrDigit(character)
            ? char.ToLowerInvariant(character)
            : '-').ToArray()).Trim('-');

    private static string ShortDigest(string value)
        => string.IsNullOrWhiteSpace(value) ? "unavailable" : value[..Math.Min(19, value.Length)];
}
