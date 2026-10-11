using System.Globalization;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

/// <summary>Catalog and existing-Lifestyle entry point for SR5 creation.</summary>
public sealed class CreationLifestylesPage : NativePageBase
{
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };
    private CharacterCreationLifestylesInteractionLoadResult? _loaded;
    private bool _loading;
    private CharacterCreationLifestylesInteractionState? _authority;
    private CharacterOverviewState? _display;
    private VerticalStackLayout _technicalDetails = new() { Spacing = 6 };
    private CancellationTokenSource? _forwardLifetime;
    private bool _nextUnavailable;
    internal bool IsPreparedDisplayCurrent => _display is { } display
        && Coordinator.IsCreationCatalogDisplayCurrent(display)
        && _authority is { } state && CreationLifestylesPhoneAuthority.IsReady(state, display);

    public CreationLifestylesPage(RunnerSessionCoordinator coordinator) : this(coordinator, null, null) { }

    internal CreationLifestylesPage(RunnerSessionCoordinator coordinator,
        CharacterCreationLifestylesInteractionState? authority, CharacterOverviewState? display) : base(coordinator)
    {
        _authority = authority;
        _display = display;
        Title = CreationFlowStrings.Get("Lifestyles.PageTitle", "Creation lifestyles");
        AutomationId = "creation-lifestyles-page";
        Content = new ScrollView { Content = _body };
    }

    protected override async Task PrepareForAppearanceRefreshAsync(CancellationToken cancellationToken)
    {
        if (IsPreparedDisplayCurrent) return;
        _authority = null;
        _display = null;
        _loaded = null;
        _loading = true;
        Refresh();
        try
        {
            var display = Coordinator.State;
            var loaded = await Coordinator.LoadCreationLifestylesForDisplayAsync(display, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!Coordinator.IsCreationCatalogDisplayCurrent(display)) return;
            _loaded = loaded;
            _authority = loaded.State;
            _display = display;
        }
        finally { if (!cancellationToken.IsCancellationRequested) _loading = false; }
    }

    protected override void Refresh()
    {
        _body.Clear();
        _technicalDetails = new() { Spacing = 6 };
        _body.Add(NativeTheme.Eyebrow(CreationFlowStrings.Get(
            "Lifestyles.Eyebrow",
            "Character creation · Contacts/Lifestyles")));
        _body.Add(NativeTheme.Title(CreationFlowStrings.Get("Lifestyles.Heading", "Lifestyles")));
        _body.Add(NativeTheme.Body(
            CreationFlowStrings.Get(
                "Lifestyles.Intro",
                "Choose where your runner lives, review the cost, then continue to starting cash."),
            NativeTheme.Muted));

        if (_loading)
        {
            _body.Add(new ActivityIndicator { IsRunning = true, AutomationId = "creation-lifestyles-loading" });
            return;
        }
        var load = _loaded;
        if (_authority is not { } state || _display is not { } display
            || !Coordinator.IsCreationCatalogDisplayCurrent(display)
            || !CreationLifestylesPhoneAuthority.IsBound(state, display))
        {
            AddBlockers(
                CreationFlowStrings.Get(
                    "Lifestyles.AuthorityUnavailable",
                    "Creation Lifestyles authority unavailable"),
                load?.Blockers ?? [CharacterCreationLifestylesBlockers.AuthorityUnavailable],
                "creation-lifestyles-unavailable");
            return;
        }

        AddBinding(state);
        AddBudget(state.Budget, "creation-lifestyles-budget");
        if (!CreationLifestylesPhoneAuthority.IsReady(state, Coordinator.State))
        {
            AddBlockers(
                CreationFlowStrings.Get(
                    "Lifestyles.AuthorityBlocked",
                    "Creation Lifestyles authority blocked"),
                state.Blockers.DefaultIfEmpty(CharacterCreationLifestylesBlockers.AuthorityUnavailable).ToArray(),
                "creation-lifestyles-blockers");
            return;
        }

        AddExisting(state);
        AddContinue(state, display);
        AddCatalog(state);
        AddAuthority(state);
        long appearance = CaptureAppearanceGeneration();
        _body.Add(NativeTheme.TechnicalDetails(_technicalDetails, "creation-lifestyles-details",
            () => Current(state, display, appearance)));
    }

    protected override void OnDisappearing()
    {
        _forwardLifetime?.Cancel();
        base.OnDisappearing();
    }

    private bool Current(CharacterCreationLifestylesInteractionState state, CharacterOverviewState display, long appearance)
        => IsCurrentAppearanceGeneration(appearance) && ReferenceEquals(_authority, state)
            && ReferenceEquals(Navigation.NavigationStack.LastOrDefault(), this)
            && Coordinator.IsCreationCatalogDisplayCurrent(display)
            && CreationLifestylesPhoneAuthority.IsReady(state, display);

    private void AddContinue(CharacterCreationLifestylesInteractionState state, CharacterOverviewState display)
    {
        if (_nextUnavailable) _body.Add(NativeTheme.Body(CreationFlowStrings.Get("Finalization.Blocked",
            "Finish the required steps and resolve the warnings before reviewing your runner."), NativeTheme.Danger));
        var next = NativeTheme.PrimaryButton(CreationFlowStrings.Get("Lifestyles.ContinueCash", "Continue to starting cash"));
        next.AutomationId = "creation-lifestyles-continue-starting-cash";
        long appearance = CaptureAppearanceGeneration();
        next.Clicked += async (_, _) => await RunAsync(async () =>
        {
            if (_forwardLifetime is not null || !Current(state, display, appearance)) return;
            using var lifetime = new CancellationTokenSource();
            _forwardLifetime = lifetime;
            next.IsEnabled = false;
            next.Text = CreationFlowStrings.Get("Lifestyles.OpeningCash", "Opening starting cash…");
            try
            {
                var loaded = await Task.Run(() => Coordinator.LoadCreationFinalizationInBackground(display, lifetime.Token));
                if (!Current(state, display, appearance) || lifetime.IsCancellationRequested) return;
                if (loaded is { Outcome: CharacterCreationFinalizationOutcomes.Available, Value.CanReview: true }
                    && Coordinator.IsCreationFinalizationStateCurrent(loaded.Value))
                    await Navigation.PushAsync(new CreationStartingCashPage(Coordinator, loaded.Value), animated: false);
                else _nextUnavailable = true;
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            finally
            {
                if (ReferenceEquals(_forwardLifetime, lifetime)) _forwardLifetime = null;
            }
        });
        _body.Add(next);
    }

    private void AddBinding(CharacterCreationLifestylesInteractionState state)
    {
        Label binding = NativeTheme.Body(
            CreationFlowStrings.Format(
                "Common.Binding",
                "Revision {0} · saved {1} · snapshot {2} · source {3}",
                state.Binding.ContentRevision,
                state.Binding.SavedRevision,
                ShortDigest(state.SnapshotDigest),
                ShortDigest(state.Binding.SourceDigest)),
            NativeTheme.Muted);
        binding.AutomationId = "creation-lifestyles-binding";
        _technicalDetails.Add(binding);
    }

    private void AddBudget(CharacterCreationLifestyleBudget budget, string automationId)
    {
        VerticalStackLayout card = new() { Spacing = 7 };
        card.Add(NativeTheme.Eyebrow(CreationFlowStrings.Get("Lifestyles.Budget", "Starting nuyen budget")));
        card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Common.Total", "Total"), budget.Total.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Common.Used", "Used"), budget.Used.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Common.Remaining", "Remaining"), budget.Remaining.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Lifestyles.Overspend", "Overspend"), budget.Overspend.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Body(
            budget.IsExact
                ? CreationFlowStrings.Get("Common.ExactCoreBudget", "Exact Core budget")
                : CreationFlowStrings.Get("Common.BudgetInexact", "Budget authority is not exact"),
            budget.IsExact ? NativeTheme.Muted : NativeTheme.Danger));
        Border border = NativeTheme.Card(card);
        border.AutomationId = automationId;
        SemanticProperties.SetDescription(
            border,
            CreationFlowStrings.Format(
                "Lifestyles.BudgetSemantic",
                "Starting nuyen. Total {0}. Used {1}. Remaining {2}.",
                budget.Total,
                budget.Used,
                budget.Remaining));
        _body.Add(border);
    }

    private void AddExisting(CharacterCreationLifestylesInteractionState state)
    {
        _body.Add(NativeTheme.Eyebrow(CreationFlowStrings.Get("Lifestyles.Current", "Current Lifestyles")));
        if (state.Lifestyles.Count == 0)
        {
            Label empty = NativeTheme.Body(
                CreationFlowStrings.Get("Lifestyles.Empty", "No Lifestyle selected yet."),
                NativeTheme.Muted);
            empty.AutomationId = "creation-lifestyles-empty";
            _body.Add(NativeTheme.Card(empty));
            return;
        }

        var display = _display!;
        long appearance = CaptureAppearanceGeneration();
        foreach (CharacterCreationLifestyleProjection lifestyle in state.Lifestyles
                     .OrderBy(item => item.Configuration.Name, StringComparer.Ordinal))
        {
            _body.Add(NativeTheme.NavigationRow(
                lifestyle.Configuration.Name,
                CreationFlowStrings.Format(
                    "Lifestyles.ExistingDetail",
                    "{0} · {1} nuyen",
                    lifestyle.BaseLifestyleName,
                    lifestyle.Economics.TotalCost.ToString(CultureInfo.InvariantCulture)),
                () => Current(state, display, appearance)
                    ? RunAsync(() => Navigation.PushAsync(new CreationLifestyleEditPage(
                        Coordinator, lifestyle.Configuration.LifestyleId, optionId: null, isCreate: false)))
                    : Task.CompletedTask,
                automationId: $"creation-lifestyle-item-{lifestyle.Configuration.LifestyleId:N}"));
        }
    }

    private void AddCatalog(CharacterCreationLifestylesInteractionState state)
    {
        _body.Add(NativeTheme.Eyebrow(CreationFlowStrings.Get(
            "Lifestyles.AddFromCatalog",
            "Add from exact Core catalog")));
        var display = _display!;
        long appearance = CaptureAppearanceGeneration();
        foreach (CharacterCreationLifestyleCatalogOption option in state.Authority.LifestyleOptions
                     .OrderBy(item => item.BaseCost)
                     .ThenBy(item => item.Name, StringComparer.Ordinal))
        {
            bool enabled = option.IsSelectable && option.EligibilityIsExact && option.Blockers.Count == 0;
            string detail = enabled
                ? CreationFlowStrings.Format(
                    "Lifestyles.CatalogDetail",
                    "{0} per {1} · {2} {3}",
                    option.BaseCost.ToString(CultureInfo.InvariantCulture),
                    option.DefaultIncrementId,
                    option.SourceBook,
                    option.Page)
                : option.Blockers.FirstOrDefault() ?? CharacterCreationLifestylesBlockers.UnsupportedSemantics;
            Guid lifestyleId = Guid.NewGuid();
            _body.Add(NativeTheme.NavigationRow(
                option.Name,
                detail,
                () => enabled && Current(state, display, appearance)
                    ? RunAsync(() => Navigation.PushAsync(new CreationLifestyleEditPage(
                        Coordinator, lifestyleId, option.OptionId, isCreate: true)))
                    : Task.CompletedTask,
                enabled: enabled,
                automationId: $"creation-lifestyle-catalog-{Token(option.OptionId)}"));
        }
    }

    private void AddAuthority(CharacterCreationLifestylesInteractionState state)
    {
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Eyebrow(CreationFlowStrings.Get("Common.AuthorityBinding", "Authority binding")));
        card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Lifestyles.Profile", "Profile"), state.Authority.SettingsProfileId));
        card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Lifestyles.Options", "Lifestyle options"), state.Authority.LifestyleOptions.Count.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Lifestyles.QualityOptions", "Quality options"), state.Authority.QualityOptions.Count.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Common.Rules", "Rules"), state.Binding.RulesDigest));
        card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Common.Runtime", "Runtime"), state.Binding.RuntimeDigest));
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-lifestyles-authority";
        _technicalDetails.Add(border);
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
        => string.IsNullOrWhiteSpace(value)
            ? CreationFlowStrings.Get("Common.Unavailable", "unavailable")
            : value[..Math.Min(19, value.Length)];
}
