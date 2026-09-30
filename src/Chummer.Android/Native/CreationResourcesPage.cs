using System.Globalization;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;
#if CHUMMER_API36_PROOF_INSTRUMENTATION
using Chummer.Android.Proof;
#endif

namespace Chummer.Android.Native;

/// <summary>
/// Phone catalog for the typed SR5 Resources authority. Every displayed amount
/// comes from Core and the only mutation route is preview plus explicit confirm.
/// </summary>
public sealed class CreationResourcesPage : NativePageBase
{
    private readonly ICharacterCreationResourcesInteractionPresenter _resources;
    private readonly ICharacterOverviewPresenter _overview;
    private readonly ICharacterCreationGearInteractionPresenter? _gear;
    private readonly AndroidSurfaceCopy _copy;
    private CharacterCreationResourcesInteractionState? _authority;
    private CharacterOverviewState? _loadedDisplay;
    private bool _ready;
    private bool _loading = true;
    private IReadOnlyList<string> _loadBlockers = [];
    private readonly VerticalStackLayout _technicalDetails = new() { Spacing = 10 };
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };

    public CreationResourcesPage(
        RunnerSessionCoordinator coordinator,
        ICharacterCreationResourcesInteractionPresenter resources,
        ICharacterOverviewPresenter overview) : this(coordinator, resources, overview, null, null)
    {
    }

    public CreationResourcesPage(
        RunnerSessionCoordinator coordinator,
        ICharacterCreationResourcesInteractionPresenter resources,
        ICharacterOverviewPresenter overview,
        ICharacterCreationGearInteractionPresenter? gear,
        CharacterCreationResourcesInteractionState? authority = null) : base(coordinator)
    {
        _resources = resources ?? throw new ArgumentNullException(nameof(resources));
        _overview = overview ?? throw new ArgumentNullException(nameof(overview));
        _gear = gear;
        _authority = authority;
        _copy = AndroidSurfaceStrings.Resolve();
        Title = _copy["Resources.PageTitle"];
        AutomationId = "creation-resources-page";
        Content = new ScrollView { Content = _body };
    }

    protected override async Task PrepareForAppearanceRefreshAsync(CancellationToken ct)
    {
        _loading = true;
        _ready = false;
        _loadedDisplay = null;
        Refresh();
        CharacterOverviewState original = Coordinator.State;
        try
        {
            var candidate = _authority;
            var prepared = await Task.Run(() => Coordinator.ReadCreationAuthority(original, () =>
            {
                var load = candidate is not null && CreationResourcesPhoneAuthority.IsReady(candidate, original)
                    ? new CharacterCreationResourcesInteractionLoadResult(
                        CharacterCreationResourcesOutcomes.Available, candidate, [])
                    : _resources.Load(original);
                return (Load: load, Ready: load.State is { } state && CreationResourcesPhoneAuthority.IsReady(state, original));
            }, ct), ct);
            ct.ThrowIfCancellationRequested();
            _loadedDisplay = original;
            _authority = prepared.Load.State;
            _loadBlockers = prepared.Load.Blockers;
            _ready = prepared.Ready;
        }
        finally { if (!ct.IsCancellationRequested) _loading = false; }
    }

    protected override void Refresh()
    {
        _body.Clear();
        _technicalDetails.Clear();
        _technicalDetails.IsVisible = false;
        _body.Add(NativeTheme.Eyebrow(_copy["Resources.Eyebrow"]));
        _body.Add(NativeTheme.Title(_copy["Resources.Title"]));
        _body.Add(NativeTheme.Body(_copy["Resources.Intro"], NativeTheme.Muted));

        if (_loading)
        {
            _body.Add(new ActivityIndicator { IsRunning = true, AutomationId = "creation-resources-loading" });
            return;
        }
        if (_loadedDisplay is not { } original || !Coordinator.IsCreationCatalogDisplayCurrent(original)
            || _authority is not { } state)
        {
            AddBlockers(_copy["Resources.AuthorityUnavailable"],
                _loadBlockers.DefaultIfEmpty(CharacterCreationResourcesBlockers.AuthorityUnavailable).ToArray(),
                "creation-resources-unavailable");
            return;
        }

        AddBudget(state.Budget, _copy["Resources.CurrentBudget"], "creation-resources-budget");
        AddBinding(state);
        if (!_ready)
        {
            AddBlockers(
                _copy["Resources.AuthorityBlocked"],
                state.Blockers.DefaultIfEmpty(CharacterCreationResourcesBlockers.AuthorityUnavailable).ToArray(),
                "creation-resources-blockers");
            AddTechnicalDetailsDisclosure();
            return;
        }

        if (state.PendingDraft is { } pending)
        {
            VerticalStackLayout saved = new() { Spacing = 6 };
            saved.Add(NativeTheme.Eyebrow(_copy["Resources.SavedDraft"]));
            saved.Add(NativeTheme.Metric(_copy["Resources.KarmaInvested"], pending.KarmaInvestment.ToString(_copy.DisplayCulture)));
            saved.Add(NativeTheme.Metric(_copy["Common.DraftRevision"], pending.DraftRevision.ToString(_copy.DisplayCulture)));
            _technicalDetails.Add(ExactValue("Resources.Option", "creation-resources-saved-option-id", pending.SelectedOptionId));
            _technicalDetails.Add(ExactValue(
                "Common.DraftRevision",
                "creation-resources-saved-draft-revision",
                pending.DraftRevision.ToString(CultureInfo.InvariantCulture)));
            _technicalDetails.Add(ExactValue("Resources.DraftDigest", "creation-resources-saved-draft-digest", pending.DraftDigest));
            Border savedCard = NativeTheme.Card(saved);
            savedCard.AutomationId = "creation-resources-saved-draft";
            _body.Add(savedCard);
        }

        AddTechnicalDetailsDisclosure();
        _body.Add(NativeTheme.Eyebrow(_copy["Resources.ConversionOptions"]));
        foreach (CharacterCreationResourceAllocationOption option in state.Options
                     .OrderBy(item => item.KarmaInvestment))
        {
            bool enabled = option.IsEnabled && option.Blockers.Count == 0;
            string detail = enabled
                ? _copy.Format(
                    "Resources.OptionDetail",
                    option.KarmaInvestment,
                    option.NuyenFromKarma.ToString("N0", _copy.DisplayCulture),
                    option.TotalStartingNuyen.ToString("N0", _copy.DisplayCulture))
                : option.Blockers.FirstOrDefault() ?? CharacterCreationResourcesBlockers.InvalidOption;
            _body.Add(NativeTheme.NavigationRow(
                option.KarmaInvestment == 0
                    ? _copy["Resources.KeepAllKarma"]
                    : _copy.Format("Resources.ConvertKarma", option.KarmaInvestment),
                detail,
                () => OpenPreviewAsync(state, option.OptionId),
                enabled,
                $"creation-resources-option-{Token(option.OptionId)}"));
        }

        AddGearRoute(state);
        AddAuthority(state);
#if CHUMMER_API36_PROOF_INSTRUMENTATION
        Api36ProofStatePublisher.TryPublishCreationResources(
            this,
            Coordinator,
            state.Binding,
            state.PendingDraft,
            state.Budget,
            state.SnapshotDigest);
#endif
    }

    private void AddGearRoute(CharacterCreationResourcesInteractionState state)
    {
        _body.Add(NativeTheme.Eyebrow(_copy["Resources.Purchases"]));
        bool enabled = state.PendingDraft is not null && _gear is not null;
        string detail = state.PendingDraft is null
            ? _copy["Resources.ConfirmBeforeGear"]
            : _gear is null
                ? _copy["Resources.GearUnavailable"]
                : _copy.Format(
                    "Resources.OpenGearDetail",
                    state.Budget.RemainingNuyen.ToString("N0", _copy.DisplayCulture));
        _body.Add(NativeTheme.NavigationRow(
            _copy["Resources.ChooseGear"],
            detail,
            () => enabled && _loadedDisplay is { } original && ReferenceEquals(state, _authority)
                && Coordinator.IsCreationCatalogDisplayCurrent(original)
                ? Navigation.PushAsync(new CreationGearPage(Coordinator, _gear!, _overview))
                : Task.CompletedTask,
            enabled,
            "creation-resources-open-gear"));
    }

    private async Task OpenPreviewAsync(
        CharacterCreationResourcesInteractionState state,
        string optionId)
    {
        if (_loadedDisplay is not { } original || !ReferenceEquals(state, _authority)
            || !Coordinator.IsCreationCatalogDisplayCurrent(original)) return;
        var review = await Task.Run(() => Coordinator.ReadCreationAuthority(original, () =>
        {
            var candidate = _resources.Prepare(original, optionId);
            return (Result: candidate, Exact: candidate.PreparedPreview is { } preview
                && CreationResourcesPhoneAuthority.PreparedMatches(preview, state, original));
        }, CancellationToken.None));
        var result = review.Result;
        if (result.PreparedPreview is not { } prepared
            || !string.Equals(result.Outcome, CharacterCreationResourcesOutcomes.Available, StringComparison.Ordinal)
            || !review.Exact || !Coordinator.IsCreationCatalogDisplayCurrent(original))
        {
            string blocker = result.Blockers.FirstOrDefault()
                             ?? CharacterCreationResourcesInteractionBlockers.PreparedPreviewMismatch;
            await DisplayAlertAsync(
                _copy["Resources.PreviewUnavailable"],
                blocker,
                _copy["Common.Ok"]);
            Refresh();
            return;
        }

        await Navigation.PushAsync(new CreationResourcesPreviewPage(
            Coordinator,
            _resources,
            _overview,
            prepared,
            _copy,
            original));
    }

    private void AddBinding(CharacterCreationResourcesInteractionState state)
    {
        Label binding = NativeTheme.Body(_copy.Format(
            "Resources.Binding",
            state.Binding.ContentRevision,
            state.Binding.SavedRevision,
            ShortDigest(state.SnapshotDigest),
            ShortDigest(state.Binding.SourceDigest)), NativeTheme.Muted);
        binding.AutomationId = "creation-resources-binding";
        _technicalDetails.Add(binding);
        _technicalDetails.Add(ExactValue(
            "Common.WorkspaceRevision",
            "creation-resources-binding-content-revision",
            state.Binding.ContentRevision.ToString(CultureInfo.InvariantCulture)));
        _technicalDetails.Add(ExactValue(
            "Resources.SavedRevision",
            "creation-resources-binding-saved-revision",
            state.Binding.SavedRevision.ToString(CultureInfo.InvariantCulture)));
        _technicalDetails.Add(ExactValue("Resources.SnapshotDigest", "creation-resources-binding-snapshot-digest", state.SnapshotDigest));
        _technicalDetails.Add(ExactValue(
            "Resources.CharacterDigest",
            "creation-resources-binding-raw-character-xml-digest",
            state.Binding.RawCharacterXmlDigest));
        _technicalDetails.Add(ExactValue(
            "Resources.AuxiliaryDigest",
            "creation-resources-binding-auxiliary-state-digest",
            state.Binding.AuxiliaryStateDigest));
        _technicalDetails.Add(ExactValue(
            "Resources.PrerequisiteDigest",
            "creation-resources-binding-prerequisite-draft-digest",
            state.Binding.PrerequisiteDraftDigest));
    }

    private void AddBudget(
        CharacterCreationResourcesBudget budget,
        string title,
        string automationId)
    {
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Eyebrow(title));
        card.Add(NativeTheme.Metric(_copy["Resources.PriorityNuyen"], Nuyen(budget.PriorityNuyen)));
        card.Add(NativeTheme.Metric(_copy["Resources.KarmaInvested"], budget.KarmaInvestment.ToString(_copy.DisplayCulture)));
        card.Add(NativeTheme.Metric(_copy["Resources.StartingNuyen"], Nuyen(budget.TotalStartingNuyen)));
        card.Add(NativeTheme.Metric(_copy["Resources.KnownPurchases"], Nuyen(budget.KnownPurchaseCost)));
        card.Add(NativeTheme.Metric(_copy["Common.Remaining"], Nuyen(budget.RemainingNuyen)));
        card.Add(NativeTheme.Metric(_copy["Resources.CarryoverLimit"], Nuyen(budget.CarryoverLimit)));
        card.Add(NativeTheme.Body(
            budget.IsExact ? _copy["Resources.ExactBudget"] : _copy["Resources.IncompleteBudget"],
            budget.IsExact ? NativeTheme.Muted : NativeTheme.Danger));
        _technicalDetails.Add(ExactValue(
            "Resources.PriorityNuyen",
            $"{automationId}-priority-nuyen",
            budget.PriorityNuyen.ToString(CultureInfo.InvariantCulture)));
        _technicalDetails.Add(ExactValue(
            "Resources.KarmaInvested",
            $"{automationId}-karma-investment",
            budget.KarmaInvestment.ToString(CultureInfo.InvariantCulture)));
        _technicalDetails.Add(ExactValue(
            "Resources.StartingNuyen",
            $"{automationId}-total-starting-nuyen",
            budget.TotalStartingNuyen.ToString(CultureInfo.InvariantCulture)));
        foreach (string blocker in budget.Blockers)
            card.Add(NativeTheme.Body(blocker, NativeTheme.Danger));
        Border border = NativeTheme.Card(card);
        border.AutomationId = automationId;
        SemanticProperties.SetDescription(
            border,
            _copy.Format("Resources.BudgetSemantic", budget.TotalStartingNuyen, budget.RemainingNuyen));
        _body.Add(border);
    }

    private void AddAuthority(CharacterCreationResourcesInteractionState state)
    {
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Eyebrow(_copy["Resources.CoreAuthority"]));
        card.Add(NativeTheme.Metric(_copy["Resources.BuildMethod"], state.Authority.BuildMethod));
        card.Add(NativeTheme.Metric(_copy["Resources.ResourceRank"], state.PendingDraft?.FinalizationContribution.PriorityRank
            ?? state.PrerequisiteDraft?.Assignments.SingleOrDefault(assignment => string.Equals(
                assignment.CategoryId,
                CharacterCreationPriorityCategoryIds.Resources,
                StringComparison.Ordinal))?.Rank
            ?? _copy["Common.Unavailable"]));
        card.Add(NativeTheme.Metric(_copy["Resources.MaximumConversion"], state.Authority.MaximumKarmaInvestment.ToString(_copy.DisplayCulture)));
        card.Add(NativeTheme.Metric(_copy["Resources.MaximumAvailability"], state.Authority.MaximumAvailability.ToString(_copy.DisplayCulture)));
        _technicalDetails.Add(ExactValue("Resources.CoreAuthority", "creation-resources-authority-digest", state.Binding.AuthorityDigest));
        _technicalDetails.Add(ExactValue("Resources.SourceDigest", "creation-resources-source-digest", state.Binding.SourceDigest));
        _technicalDetails.Add(ExactValue("Common.Rules", "creation-resources-rules-digest", state.Binding.RulesDigest));
        _technicalDetails.Add(ExactValue("Common.Runtime", "creation-resources-runtime-digest", state.Binding.RuntimeDigest));
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-resources-authority";
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

    private string Nuyen(decimal value)
        => value.ToString("N0", _copy.DisplayCulture) + " ¥";

    private static string Token(string value)
        => new string(value.Select(character => char.IsLetterOrDigit(character)
            ? char.ToLowerInvariant(character)
            : '-').ToArray()).Trim('-');

    private string ShortDigest(string value)
        => string.IsNullOrWhiteSpace(value) ? _copy["Common.Unavailable"] : value[..Math.Min(19, value.Length)];

    private void AddTechnicalDetailsDisclosure()
    {
        Button toggle = NativeTheme.SecondaryButton(_copy["Resources.ShowDetails"]);
        toggle.AutomationId = "creation-resources-technical-details-toggle";
        toggle.Clicked += (_, _) =>
        {
            // A refresh replaces the button but retains the details container.
            // A queued click from the detached tree must not toggle its successor.
            if (!ReferenceEquals(toggle.Parent, _body))
                return;
            _technicalDetails.IsVisible = !_technicalDetails.IsVisible;
            toggle.Text = _copy[_technicalDetails.IsVisible
                ? "Resources.HideDetails"
                : "Resources.ShowDetails"];
        };
        _body.Add(toggle);
        _body.Add(_technicalDetails);
    }

    private View ExactValue(string labelKey, string automationId, string value)
    {
        Label label = NativeTheme.Body(value, NativeTheme.Muted);
        label.AutomationId = automationId;
        label.LineBreakMode = LineBreakMode.CharacterWrap;
        return new VerticalStackLayout
        {
            Spacing = 2,
            Children = { NativeTheme.FieldLabel(_copy[labelKey]), label }
        };
    }
}

public sealed class CreationResourcesPreviewPage : NativePageBase
{
    private readonly ICharacterCreationResourcesInteractionPresenter _resources;
    private readonly ICharacterOverviewPresenter _overview;
    private readonly CharacterCreationResourcesPreparedPreview _prepared;
    private readonly CharacterOverviewState _original;
    private bool _ready;
    private bool _loading = true;
    private bool _submitted;
    private readonly AndroidSurfaceCopy _copy;
    private VerticalStackLayout _technicalDetails = new() { Spacing = 6 };
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };
    private CharacterCreationResourcesReceipt? _receipt;
    private string? _failure;

    public CreationResourcesPreviewPage(
        RunnerSessionCoordinator coordinator,
        ICharacterCreationResourcesInteractionPresenter resources,
        ICharacterOverviewPresenter overview,
        CharacterCreationResourcesPreparedPreview prepared,
        AndroidSurfaceCopy copy, CharacterOverviewState? original = null) : base(coordinator)
    {
        _resources = resources ?? throw new ArgumentNullException(nameof(resources));
        _overview = overview ?? throw new ArgumentNullException(nameof(overview));
        _prepared = prepared ?? throw new ArgumentNullException(nameof(prepared));
        _original = original ?? coordinator.State;
        _copy = copy ?? throw new ArgumentNullException(nameof(copy));
        Title = _copy["ResourcesPreview.PageTitle"];
        AutomationId = "creation-resources-preview-page";
        Content = new ScrollView { Content = _body };
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
                var load = _resources.Load(_original);
                return load.State is { } current
                    && CreationResourcesPhoneAuthority.PreparedMatches(_prepared, current, _original);
            }, ct), ct);
        }
        finally { if (!ct.IsCancellationRequested) _loading = false; }
    }

    protected override void Refresh()
    {
        _body.Clear();
        _technicalDetails = new() { Spacing = 6 };
        _body.Add(NativeTheme.Eyebrow(_copy["ResourcesPreview.Eyebrow"]));
        _body.Add(NativeTheme.Title(_copy["ResourcesPreview.Title"]));
        if (!Coordinator.CanDisplayCreationPurchase(_original))
        {
            _body.Add(NativeTheme.Body(_copy["ResourcesPreview.ConfirmStale"], NativeTheme.Danger));
            return;
        }
        if (_loading)
        {
            _body.Add(new ActivityIndicator { IsRunning = true, AutomationId = "creation-resources-preview-loading" });
            return;
        }
        AddBudgetComparison();
        AddContribution();

        if (_receipt is { } receipt)
        {
            VerticalStackLayout applied = new() { Spacing = 6 };
            applied.Add(NativeTheme.Eyebrow(_copy["ResourcesPreview.Saved"]));
            if (_failure is not null) applied.Add(NativeTheme.Body(_failure, NativeTheme.Muted));
            _technicalDetails.Add(NativeTheme.Metric(_copy["Common.Receipt"], receipt.ReceiptId));
            _technicalDetails.Add(NativeTheme.Metric(_copy["Common.WorkspaceRevision"], receipt.WorkspaceRevision.ToString(_copy.DisplayCulture)));
            _technicalDetails.Add(NativeTheme.Metric(_copy["Common.DraftRevision"], receipt.DraftRevision.ToString(_copy.DisplayCulture)));
            _technicalDetails.Add(NativeTheme.Body(_copy.Format("ResourcesPreview.ReceiptDigest", ShortDigest(receipt.ReceiptDigest)), NativeTheme.Muted));
            _technicalDetails.Add(ExactValue("creation-resources-receipt-option-id", receipt.OptionId));
            _technicalDetails.Add(ExactValue(
                "creation-resources-receipt-workspace-revision",
                receipt.WorkspaceRevision.ToString(CultureInfo.InvariantCulture)));
            _technicalDetails.Add(ExactValue(
                "creation-resources-receipt-saved-revision",
                receipt.SavedRevision.ToString(CultureInfo.InvariantCulture)));
            _technicalDetails.Add(ExactValue(
                "creation-resources-receipt-draft-revision",
                receipt.DraftRevision.ToString(CultureInfo.InvariantCulture)));
            _technicalDetails.Add(ExactValue(
                "creation-resources-receipt-total-starting-nuyen",
                receipt.TotalStartingNuyen.ToString(CultureInfo.InvariantCulture)));
            _technicalDetails.Add(ExactValue("creation-resources-receipt-preview-digest", receipt.PreviewDigest));
            _technicalDetails.Add(ExactValue("creation-resources-receipt-draft-digest", receipt.DraftDigest));
            _technicalDetails.Add(ExactValue("creation-resources-receipt-digest", receipt.ReceiptDigest));
            Border receiptCard = NativeTheme.Card(applied);
            receiptCard.AutomationId = "creation-resources-confirm-receipt";
            _body.Add(receiptCard);
            _body.Add(NativeTheme.NavigationRow(
                _copy["ResourcesPreview.Back"],
                _copy["ResourcesPreview.Reopen"],
                () => Navigation.PopAsync(),
                automationId: "creation-resources-reopen"));
            _body.Add(NativeTheme.TechnicalDetails(_technicalDetails, "creation-resources-preview-details"));
            return;
        }

        if (!string.IsNullOrWhiteSpace(_failure))
        {
            Label failure = NativeTheme.Body(_failure, NativeTheme.Danger);
            failure.AutomationId = "creation-resources-confirm-failed";
            _body.Add(NativeTheme.Card(failure));
        }

        bool exact = !_submitted && _ready && Coordinator.IsCreationCatalogDisplayCurrent(_original);
        Button confirm = NativeTheme.PrimaryButton(_copy["ResourcesPreview.Confirm"]);
        confirm.AutomationId = "creation-resources-confirm";
        confirm.IsEnabled = exact;
        confirm.Clicked += async (_, _) => await RunAsync(ConfirmAsync);
        _body.Add(confirm);
        Label warning = NativeTheme.Body(
            exact
                ? _copy["ResourcesPreview.ConfirmExact"]
                : _copy["ResourcesPreview.ConfirmStale"],
            exact ? NativeTheme.Muted : NativeTheme.Danger);
        warning.AutomationId = "creation-resources-confirm-authority";
        _body.Add(warning);
        _body.Add(NativeTheme.TechnicalDetails(_technicalDetails, "creation-resources-preview-details"));
    }

    private async Task ConfirmAsync()
    {
        if (_submitted || !_ready || !Coordinator.IsCreationCatalogDisplayCurrent(_original)) return;
        _submitted = true;
        _ready = false;
        _failure = null;
        Refresh();
        var result = await Coordinator.ConfirmCreationResourcesPurchaseAsync(_resources, _original, _prepared);
        if (result.Receipt is not { } receipt
            || result.Outcome is not (CharacterCreationResourcesOutcomes.Applied or CharacterCreationResourcesOutcomes.Replayed)
            || !CreationResourcesPhoneAuthority.ReceiptMatches(_prepared, receipt))
        {
            _failure = result.Outcome == "outcome-unknown"
                ? CreationFlowStrings.Get("Purchases.OutcomeUnknown", "The result could not be verified. Reopen the character before making another purchase.")
                : result.Blockers.FirstOrDefault() ?? CharacterCreationResourcesInteractionBlockers.ReceiptMismatch;
            return;
        }
        // Retain the durable receipt before refreshing. Refresh failure is not
        // mutation failure and must not permit a second submission.
        _receipt = receipt;
        if (result.RefreshedState is not { } refreshed
            || !CreationResourcesPhoneAuthority.RefreshedStateMatches(_prepared, receipt, refreshed)
            || !Coordinator.CanDisplayCreationPurchase(_original))
        {
            _failure = CreationFlowStrings.Get("Purchases.SavedReopen", "Saved. Reopen the character to refresh this view.");
            return;
        }
#if CHUMMER_API36_PROOF_INSTRUMENTATION
        if (AndroidE2EAuthority.Enabled)
        {
            if (!CreationResourcesPhoneAuthority.TryNormalizeRawCharacterXmlSha256(
                    receipt.RawCharacterXmlDigest,
                    out string expectedPayloadSha256))
            {
                _failure = CharacterCreationResourcesInteractionBlockers.RefreshAuthorityRequired;
                return;
            }
            NativeWorkspaceAuthoritySnapshot? proofAuthority =
                await Coordinator.RefreshApi36ProofWorkspaceAuthorityAsync(
                    receipt.WorkspaceId,
                    receipt.WorkspaceRevision,
                    receipt.SavedRevision,
                    expectedPayloadSha256,
                    CancellationToken.None);
            if (proofAuthority is null)
            {
                _failure = CharacterCreationResourcesInteractionBlockers.RefreshAuthorityRequired;
                return;
            }
        }
#endif
    }

    private void AddBudgetComparison()
    {
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Eyebrow(_copy["ResourcesPreview.ExactDelta"]));
        card.Add(NativeTheme.Metric(_copy["ResourcesPreview.KarmaBefore"], _prepared.BudgetBefore.KarmaInvestment.ToString(_copy.DisplayCulture)));
        card.Add(NativeTheme.Metric(_copy["ResourcesPreview.KarmaAfter"], _prepared.BudgetAfter.KarmaInvestment.ToString(_copy.DisplayCulture)));
        card.Add(NativeTheme.Metric(_copy["ResourcesPreview.StartingBefore"], Nuyen(_prepared.BudgetBefore.TotalStartingNuyen)));
        card.Add(NativeTheme.Metric(_copy["ResourcesPreview.StartingAfter"], Nuyen(_prepared.BudgetAfter.TotalStartingNuyen)));
        card.Add(NativeTheme.Metric(_copy["ResourcesPreview.RemainingAfter"], Nuyen(_prepared.BudgetAfter.RemainingNuyen)));
        card.Add(NativeTheme.Metric(_copy["ResourcesPreview.CarryoverExcess"], Nuyen(_prepared.BudgetAfter.CarryoverExcess)));
        _technicalDetails.Add(ExactValue(
            "creation-resources-preview-total-starting-nuyen",
            _prepared.BudgetAfter.TotalStartingNuyen.ToString(CultureInfo.InvariantCulture)));
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-resources-preview-budget";
        _body.Add(border);
    }

    private void AddContribution()
    {
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Eyebrow(_copy["ResourcesPreview.Finalization"]));
        card.Add(NativeTheme.Metric(_copy["ResourcesPreview.PriorityRank"], _prepared.FinalizationContribution.PriorityRank));
        card.Add(NativeTheme.Metric(_copy["ResourcesPreview.PriorityGrant"], Nuyen(_prepared.FinalizationContribution.StartingNuyen)));
        card.Add(NativeTheme.Metric(_copy["ResourcesPreview.KarmaConverted"], _prepared.FinalizationContribution.NuyenKarma.ToString(_copy.DisplayCulture)));
        _technicalDetails.Add(NativeTheme.Body(_copy.Format("ResourcesPreview.Preview", ShortDigest(_prepared.PreviewDigest)), NativeTheme.Muted));
        _technicalDetails.Add(ExactValue("creation-resources-preview-option-id", _prepared.SelectedOption.OptionId));
        _technicalDetails.Add(ExactValue(
            "creation-resources-preview-priority-grant",
            _prepared.FinalizationContribution.StartingNuyen.ToString(CultureInfo.InvariantCulture)));
        _technicalDetails.Add(ExactValue("creation-resources-preview-digest", _prepared.PreviewDigest));
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-resources-preview-contribution";
        _body.Add(border);
    }

    private string Nuyen(decimal value)
        => value.ToString("N0", _copy.DisplayCulture) + " ¥";

    private string ShortDigest(string value)
        => string.IsNullOrWhiteSpace(value) ? _copy["Common.Unavailable"] : value[..Math.Min(19, value.Length)];

    private static Label ExactValue(string automationId, string value)
    {
        Label label = NativeTheme.Body(value, NativeTheme.Muted);
        label.AutomationId = automationId;
        label.LineBreakMode = LineBreakMode.CharacterWrap;
        return label;
    }
}

internal static class CreationResourcesPhoneAuthority
{
    public static bool TryNormalizeRawCharacterXmlSha256(
        string? digest,
        out string sha256)
    {
        sha256 = string.Empty;
        if (!CharacterCreationResourcesRules.IsCanonicalDigest(digest))
        {
            return false;
        }
        sha256 = digest!.Substring("sha256:".Length, 64);
        return true;
    }

    public static bool IsBound(
        CharacterCreationResourcesInteractionState state,
        CharacterOverviewState overview)
        => overview.Profile?.Created == false
           && overview.WorkspaceId is { } workspaceId
           && overview.CreationWizard is { CharacterCreated: false } wizard
           && state.Binding.WorkspaceId == workspaceId
           && state.Binding.WorkspaceRevision == overview.ContentRevision
           && state.Binding.ContentRevision == overview.ContentRevision
           && state.Binding.SavedRevision == overview.SavedRevision
           && wizard.WorkspaceRevision == overview.ContentRevision
           && string.Equals(wizard.WorkspaceId, workspaceId.Value, StringComparison.Ordinal)
           && CharacterCreationResourcesRules.DigestsEqual(
               state.Binding.RawCharacterXmlDigest,
               wizard.ContentDigest)
           && CharacterCreationResourcesRules.IsCanonicalDigest(state.SnapshotDigest)
           && IsLowerRawSha256(state.Binding.AuxiliaryStateDigest)
           && CharacterCreationResourcesRules.IsCanonicalDigest(state.Binding.PrerequisiteDraftDigest)
           && CharacterCreationResourcesRules.IsCanonicalDigest(state.Binding.AuthorityDigest)
           && CharacterCreationResourcesRules.IsCanonicalDigest(state.Binding.SourceDigest)
           && CharacterCreationResourcesRules.IsCanonicalDigest(state.Binding.RulesDigest)
           && CharacterCreationResourcesRules.IsCanonicalDigest(state.Binding.RuntimeDigest);

    private static bool IsLowerRawSha256(string? value)
        => value is { Length: 64 }
           && value.All(static character =>
               character is >= '0' and <= '9' or >= 'a' and <= 'f');

    public static bool IsReady(
        CharacterCreationResourcesInteractionState state,
        CharacterOverviewState overview)
        => IsBound(state, overview)
           && state.CanEdit
           && state.Blockers.Count == 0
           && state.Budget.IsExact
           && state.Budget.Blockers.Count == 0
           && CharacterCreationResourcesRules.IsValidAuthority(state.Authority)
           && CharacterCreationResourcesRules.DigestsEqual(
               state.Binding.AuthorityDigest,
               state.Authority.AuthorityDigest)
           && CharacterCreationResourcesRules.DigestsEqual(
               state.Binding.SourceDigest,
               state.Authority.SourceDigest)
           && CharacterCreationResourcesRules.DigestsEqual(
               state.Binding.RulesDigest,
               state.Authority.RulesDigest)
           && CharacterCreationResourcesRules.DigestsEqual(
               state.Binding.RuntimeDigest,
               state.Authority.RuntimeDigest)
           && state.Options.Any(option => BudgetMatchesEnabledOption(option, state.Budget));

    private static bool BudgetMatchesEnabledOption(
        CharacterCreationResourceAllocationOption option,
        CharacterCreationResourcesBudget budget)
        => option.IsEnabled
           && option.Blockers.Count == 0
           && CharacterCreationResourcesRules.IsCanonicalDigest(option.OptionDigest)
           && CharacterCreationResourcesRules.DigestsEqual(
               option.OptionDigest,
               CharacterCreationResourcesRules.ComputeAllocationOptionDigest(option))
           && option.KarmaInvestment == budget.KarmaInvestment
           && option.NuyenFromKarma == budget.NuyenFromKarma
           && option.TotalStartingNuyen == budget.TotalStartingNuyen
           && budget.PriorityNuyen + budget.NuyenFromKarma == budget.TotalStartingNuyen;

    public static bool PreparedMatches(
        CharacterCreationResourcesPreparedPreview prepared,
        CharacterCreationResourcesInteractionState state,
        CharacterOverviewState overview)
        => IsReady(state, overview)
           && prepared.Binding == state.Binding
           && CharacterCreationResourcesRules.DigestsEqual(
               prepared.StateSnapshotDigest,
               state.SnapshotDigest)
           && state.Options.Count(option => string.Equals(
               option.OptionId,
               prepared.SelectedOption.OptionId,
               StringComparison.Ordinal)) == 1
           && state.Options.Any(option => string.Equals(
                   option.OptionId,
                   prepared.SelectedOption.OptionId,
                   StringComparison.Ordinal)
               && option.IsEnabled
               && option.Blockers.Count == 0
               && CharacterCreationResourcesRules.DigestsEqual(
                   option.OptionDigest,
                   prepared.SelectedOption.OptionDigest))
           && prepared.RequiresExplicitConfirmation
           && prepared.CanConfirm
           && prepared.Blockers.Count == 0
           && CharacterCreationResourcesRules.IsCanonicalDigest(prepared.PreviewDigest)
           && CharacterCreationResourcesRules.DigestsEqual(
               prepared.PreviewDigest,
               CharacterCreationResourcesRules.ComputePreviewDigest(new CharacterCreationResourcesPreview(
                   CharacterCreationResourcesSchemas.PreviewV1,
                   CharacterCreationWizardStepIds.Resources,
                   prepared.Binding,
                   prepared.Before,
                   prepared.After,
                   prepared.SelectedOption,
                   prepared.BudgetBefore,
                   prepared.BudgetAfter,
                   prepared.FinalizationContribution,
                   prepared.Blockers,
                   prepared.RequiresExplicitConfirmation,
                   prepared.CanConfirm,
                   prepared.PreviewDigest)))
           && prepared.IdempotencyKey is { Length: > 0 and <= 200 };

    public static bool ReceiptMatches(
        CharacterCreationResourcesPreparedPreview prepared,
        CharacterCreationResourcesReceipt receipt)
        => receipt.WorkspaceId == prepared.Binding.WorkspaceId
           && receipt.PreviousWorkspaceRevision == prepared.Binding.WorkspaceRevision
           && receipt.WorkspaceRevision == receipt.PreviousWorkspaceRevision + 1
           && receipt.PreviousSavedRevision == prepared.Binding.SavedRevision
           && receipt.SavedRevision == receipt.WorkspaceRevision
           && string.Equals(receipt.OptionId, prepared.SelectedOption.OptionId, StringComparison.Ordinal)
           && receipt.KarmaInvestment == prepared.SelectedOption.KarmaInvestment
           && receipt.TotalStartingNuyen == prepared.BudgetAfter.TotalStartingNuyen
           && receipt.RemainingNuyen == prepared.BudgetAfter.RemainingNuyen
           && receipt.DraftRevision == prepared.After.DraftRevision
           && CharacterCreationResourcesRules.DigestsEqual(receipt.PreviewDigest, prepared.PreviewDigest)
           && !receipt.CharacterDocumentChanged
           && CharacterCreationResourcesRules.IsCanonicalDigest(receipt.DraftDigest)
           && CharacterCreationResourcesRules.IsCanonicalDigest(receipt.ReceiptDigest)
           && CharacterCreationResourcesRules.DigestsEqual(
               receipt.ReceiptDigest,
               CharacterCreationResourcesRules.ComputeReceiptDigest(receipt));

    public static bool RefreshedStateMatches(
        CharacterCreationResourcesPreparedPreview prepared,
        CharacterCreationResourcesReceipt receipt,
        CharacterCreationResourcesInteractionState refreshed)
        => refreshed.Binding.WorkspaceId == receipt.WorkspaceId
           && refreshed.Binding.WorkspaceRevision == receipt.WorkspaceRevision
           && refreshed.Binding.ContentRevision == receipt.WorkspaceRevision
           && refreshed.Binding.SavedRevision == receipt.SavedRevision
           && CharacterCreationResourcesRules.DigestsEqual(
               refreshed.Binding.RawCharacterXmlDigest,
               receipt.RawCharacterXmlDigest)
           && CharacterCreationResourcesRules.DigestsEqual(
               refreshed.Binding.PrerequisiteDraftDigest,
               receipt.PrerequisiteDraftDigest)
           && CharacterCreationResourcesRules.DigestsEqual(
               refreshed.Binding.AuthorityDigest,
               receipt.AuthorityDigest)
           && refreshed.PendingDraft is { } draft
           && draft.DraftRevision == receipt.DraftRevision
           && CharacterCreationResourcesRules.DigestsEqual(draft.DraftDigest, receipt.DraftDigest)
           && string.Equals(draft.SelectedOptionId, prepared.SelectedOption.OptionId, StringComparison.Ordinal)
           && refreshed.Budget.TotalStartingNuyen == prepared.BudgetAfter.TotalStartingNuyen
           && refreshed.Budget.RemainingNuyen == prepared.BudgetAfter.RemainingNuyen;
}
