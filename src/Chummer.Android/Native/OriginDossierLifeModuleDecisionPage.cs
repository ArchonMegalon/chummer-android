using System.Globalization;
using Chummer.Contracts.Characters;
using Chummer.Presentation.OriginBooks;

namespace Chummer.Android.Native;

/// <summary>
/// Phone rendering for a Core-validated live Life Module decision. Callers own
/// prepare/confirm orchestration; this page never calculates or applies an
/// effect itself.
/// </summary>
internal sealed class OriginDossierLifeModuleDecisionPage : ContentPage
{
    private OriginDossierLifeModuleDecisionState _state;
    private CharacterCreationBudgetState _budget;
    private readonly string _foundationSnapshotDigest;
    private readonly string _boundContentDigest;
    private readonly string _boundSourceDigest;
    private readonly string _boundMechanicsSnapshotDigest;
    private readonly OriginDossierNarrativeLocaleBinding _locale;
    private readonly AndroidSurfaceCopy _copy;
    private readonly Func<string, Task<OriginDossierLifeModulePhoneResult?>> _prepareChoice;
    private readonly Func<string, string, Task<bool>> _confirmChoice;

    public OriginDossierLifeModuleDecisionPage(
        OriginDossierLifeModulePhoneResult opened,
        string activeAppLocale,
        Func<string, Task<OriginDossierLifeModulePhoneResult?>> prepareChoice,
        Func<string, string, Task<bool>> confirmChoice)
    {
        ArgumentNullException.ThrowIfNull(opened);
        if (!TryReadDisplayAuthority(
                opened,
                out OriginDossierLifeModuleDecisionState state,
                out CharacterCreationBudgetState budget))
        {
            throw new InvalidOperationException(
                "The Origin Dossier decision has no exact Life Modules budget authority.");
        }
        _state = state;
        _budget = budget;
        _foundationSnapshotDigest = opened.FoundationSnapshotDigest!;
        _boundContentDigest = opened.BoundContentDigest!;
        _boundSourceDigest = opened.BoundSourceDigest!;
        _boundMechanicsSnapshotDigest = opened.BoundMechanicsSnapshotDigest!;
        OriginDossierNarrativeLocaleBinding locale =
            OriginDossierNarrativeLocalePolicy.Resolve(activeAppLocale);
        _copy = AndroidSurfaceStrings.Resolve(activeAppLocale);
        if (!locale.CanRenderNarrativeLocale(_state.Locale)
            || string.IsNullOrWhiteSpace(_state.BoundTurnSeedDigest))
        {
            throw new InvalidOperationException(
                "The Origin Dossier decision is not bound to the active app language.");
        }
        _locale = locale;
        _prepareChoice = prepareChoice ?? throw new ArgumentNullException(nameof(prepareChoice));
        _confirmChoice = confirmChoice ?? throw new ArgumentNullException(nameof(confirmChoice));
        Title = _copy["Origin.PageTitle"];
        AutomationId = "origin-life-decision";
        Content = new ScrollView { Content = BuildBody() };
    }

    private VerticalStackLayout BuildBody()
    {
        var body = new VerticalStackLayout
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow(_copy.Format("Origin.StageTurn", _state.StageOrder, _state.TurnSequence)));
        body.Add(NativeTheme.Title(_state.RunnerDisplayName));
        string localeCopy = _locale.UsesEnglishFallback
            ? _copy.Format("Origin.LocaleFallback", _locale.FormattingLocale)
            : _copy.Format("Origin.Locale", _locale.ResourceLanguage.ToUpperInvariant(), _locale.FormattingLocale);
        Label locale = NativeTheme.Body(localeCopy, NativeTheme.Muted);
        locale.AutomationId = "origin-life-locale";
        SemanticProperties.SetDescription(
            locale,
            _copy.Format(
                "Origin.LocaleSemantic",
                _locale.ResourceLanguage,
                _locale.FormattingLocale,
                _copy[_locale.UsesEnglishFallback ? "Common.Yes" : "Common.No"]));
        body.Add(locale);

        var budget = new VerticalStackLayout { Spacing = 6 };
        budget.Add(NativeTheme.Eyebrow(_copy["Origin.Budget"]));
        CultureInfo formattingCulture = CultureInfo.GetCultureInfo(_locale.FormattingLocale);
        budget.Add(BudgetMetric(
            "origin-life-budget-total",
            _copy["Origin.BudgetTotal"],
            _budget.Total,
            formattingCulture));
        budget.Add(BudgetMetric(
            "origin-life-budget-used",
            _copy["Origin.BudgetUsed"],
            _budget.Used,
            formattingCulture));
        budget.Add(BudgetMetric(
            "origin-life-budget-remaining",
            _copy["Origin.BudgetRemaining"],
            _budget.Remaining,
            formattingCulture));
        Border budgetCard = NativeTheme.Card(budget);
        budgetCard.AutomationId = "origin-life-budget";
        SemanticProperties.SetDescription(
            budgetCard,
            _copy.Format(
                "Origin.BudgetSemantic",
                _budget.Total.ToString("0.##", formattingCulture),
                _budget.Used.ToString("0.##", formattingCulture),
                _budget.Remaining.ToString("0.##", formattingCulture),
                _budget.Unit));
        body.Add(budgetCard);

        Label story = NativeTheme.Body(_state.VisibleStoryMarkdown);
        story.AutomationId = "origin-life-story";
        SemanticProperties.SetDescription(
            story,
            _copy.Format("Origin.StorySemantic", _state.Locale));
        body.Add(NativeTheme.Card(story));
        Label prompt = NativeTheme.Title(_state.DecisionPrompt, 21);
        prompt.AutomationId = "origin-life-prompt";
        body.Add(prompt);

        for (int choiceIndex = 0; choiceIndex < _state.Choices.Count; choiceIndex++)
        {
            OriginDossierLifeModuleChoiceState choice = _state.Choices[choiceIndex];
            var card = new VerticalStackLayout { Spacing = 8 };
            Button select = choice.IsSelected
                ? NativeTheme.PrimaryButton(choice.Label)
                : NativeTheme.SecondaryButton(choice.Label);
            select.AutomationId = $"origin-life-choice-{choiceIndex}";
            string choiceId = choice.ChoiceId;
            select.Clicked += async (_, _) =>
            {
                OriginDossierLifeModulePhoneResult? prepared =
                    await _prepareChoice(choiceId);
                if (prepared is not null && TryAdoptPrepared(prepared))
                {
                    Content = new ScrollView { Content = BuildBody() };
                }
            };
            card.Add(select);

            Label source = NativeTheme.Body(
                string.IsNullOrWhiteSpace(choice.PageReference)
                    ? choice.Source
                    : $"{choice.Source} · {choice.PageReference}",
                NativeTheme.Muted);
            source.AutomationId = $"origin-life-choice-source-{choiceIndex}";
            card.Add(source);
            Label anchors = NativeTheme.Body(
                _copy.Format(
                    "Origin.SourceAnchors",
                    string.Join(", ", choice.SourceAnchorIds)),
                NativeTheme.Muted);
            anchors.AutomationId = $"origin-life-choice-anchors-{choiceIndex}";
            card.Add(anchors);
            card.Add(NativeTheme.Metric(_copy["Origin.Karma"], choice.KarmaRaw));

            for (int effectIndex = 0; effectIndex < choice.Effects.Count; effectIndex++)
            {
                OriginDossierLifeModuleEffectState effect = choice.Effects[effectIndex];
                Label effectLabel = NativeTheme.Body(_copy.Format(
                    "Origin.Effect",
                    RunnerSessionCoordinator.HumanizeId(effect.Domain),
                    RunnerSessionCoordinator.HumanizeId(effect.TargetId),
                    effect.BeforeValue,
                    effect.AfterValue));
                effectLabel.AutomationId = $"origin-life-effect-{choiceIndex}-{effectIndex}";
                card.Add(effectLabel);
            }
            body.Add(NativeTheme.Card(card));
        }

        Label provenance = NativeTheme.Body(
            _copy.Format("Origin.NarrativeOnly", _state.LtdProviderDisplay),
            NativeTheme.Muted);
        provenance.AutomationId = "origin-life-ltd-provenance";
        body.Add(provenance);

        if (_state.SelectedChoiceId is { } selectedChoiceId
            && _state.PendingPreviewDigest is { } previewDigest)
        {
            Label preview = NativeTheme.Body(
                _copy["Origin.Review"],
                NativeTheme.Ink);
            preview.AutomationId = "origin-life-preview";
            body.Add(preview);
            Button confirm = NativeTheme.PrimaryButton(_copy["Origin.Confirm"]);
            confirm.AutomationId = "origin-life-confirm";
            confirm.IsEnabled = _state.CanConfirm;
            confirm.Clicked += async (_, _) =>
            {
                bool completed = await _confirmChoice(selectedChoiceId, previewDigest);
                if (completed)
                    await Navigation.PopAsync();
            };
            body.Add(confirm);
        }

        return body;
    }

    private Grid BudgetMetric(
        string automationId,
        string label,
        decimal value,
        CultureInfo formattingCulture)
    {
        Grid metric = NativeTheme.Metric(
            label,
            $"{value.ToString("0.##", formattingCulture)} {_budget.Unit}");
        metric.AutomationId = automationId;
        return metric;
    }

    private bool TryAdoptPrepared(OriginDossierLifeModulePhoneResult prepared)
    {
        if (!TryReadDisplayAuthority(
                prepared,
                out OriginDossierLifeModuleDecisionState state,
                out CharacterCreationBudgetState budget)
            || !string.Equals(state.WorkspaceId, _state.WorkspaceId, StringComparison.Ordinal)
            || state.WorkspaceRevision != _state.WorkspaceRevision
            || !string.Equals(
                prepared.FoundationSnapshotDigest,
                _foundationSnapshotDigest,
                StringComparison.Ordinal)
            || !string.Equals(prepared.BoundContentDigest, _boundContentDigest, StringComparison.Ordinal)
            || !string.Equals(prepared.BoundSourceDigest, _boundSourceDigest, StringComparison.Ordinal)
            || !string.Equals(
                prepared.BoundMechanicsSnapshotDigest,
                _boundMechanicsSnapshotDigest,
                StringComparison.Ordinal)
            || budget.Total != _budget.Total
            || budget.Used != _budget.Used
            || budget.Remaining != _budget.Remaining
            || !string.Equals(budget.Unit, _budget.Unit, StringComparison.Ordinal))
        {
            return false;
        }

        _state = state;
        _budget = budget;
        return true;
    }

    private static bool TryReadDisplayAuthority(
        OriginDossierLifeModulePhoneResult result,
        out OriginDossierLifeModuleDecisionState state,
        out CharacterCreationBudgetState budget)
    {
        state = result.State!;
        budget = result.LifeModuleBudget!;
        return result.IsSuccess
               && result.State is not null
               && result.LifeModuleBudget is not null
               && string.Equals(
                   result.LifeModuleBudget.BudgetId,
                   CharacterCreationBudgetIds.LifeModules,
                   StringComparison.Ordinal)
               && result.LifeModuleBudget.IsExact
               && result.LifeModuleBudget.Blockers.Count == 0
               && !string.IsNullOrWhiteSpace(result.LifeModuleBudget.Unit)
               && !string.IsNullOrWhiteSpace(result.FoundationSnapshotDigest)
               && !string.IsNullOrWhiteSpace(result.BoundContentDigest)
               && !string.IsNullOrWhiteSpace(result.BoundSourceDigest)
               && !string.IsNullOrWhiteSpace(result.BoundMechanicsSnapshotDigest);
    }
}
