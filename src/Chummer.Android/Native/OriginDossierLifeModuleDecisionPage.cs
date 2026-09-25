using System.Globalization;
using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;
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
    private string _foundationSnapshotDigest;
    private string _boundContentDigest;
    private string _boundSourceDigest;
    private string _boundMechanicsSnapshotDigest;
    private readonly OriginDossierNarrativeLocaleBinding _locale;
    private readonly AndroidSurfaceCopy _copy;
    private Chummer.Contracts.LifeModules.LifeModuleOriginDossierDraftCheckpoint? _storyCheckpoint;
    private readonly Func<string, IReadOnlyDictionary<string, string>?, Task<OriginDossierLifeModulePhoneResult?>> _prepareChoice;
    private readonly Func<string, string, Task<OriginDossierLifeModulePhoneResult?>> _confirmChoice;
    private string? _selectedMetatypeOptionId;
    private int _renderGeneration;
    private bool _actionInFlight;
    private string? _editingChoiceId;
    private readonly Dictionary<string, string> _answers = new(StringComparer.Ordinal);

    public OriginDossierLifeModuleDecisionPage(
        OriginDossierLifeModulePhoneResult opened,
        string activeAppLocale,
        Func<string, IReadOnlyDictionary<string, string>?, Task<OriginDossierLifeModulePhoneResult?>> prepareChoice,
        Func<string, string, Task<OriginDossierLifeModulePhoneResult?>> confirmChoice)
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
        _storyCheckpoint = opened.StoryCheckpoint;
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
        _selectedMetatypeOptionId = _state.Choices.Where(choice => choice.IsSelected)
            .Select(MetatypeEffect).SingleOrDefault()?.TargetId;
        Title = _copy["Origin.PageTitle"];
        AutomationId = "origin-life-decision";
        Content = new ScrollView { Content = BuildBody() };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Content = new ScrollView { Content = BuildBody() };
    }

    protected override void OnDisappearing()
    {
        ++_renderGeneration;
        base.OnDisappearing();
    }

    private VerticalStackLayout BuildBody()
    {
        int generation = ++_renderGeneration;
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

        if (_storyCheckpoint?.Projection.VisibleChapters.Count > 0)
        {
            Button readBook = NativeTheme.SecondaryButton(_copy["Origin.ReadBook"]);
            readBook.AutomationId = "origin-life-read-book";
            readBook.Clicked += async (_, _) => await Navigation.PushAsync(
                new OriginDossierBookPage(_storyCheckpoint, _locale.FormattingLocale));
            body.Add(readBook);
        }

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

        // This is a view filter over Core's exact composite Foundation choices,
        // not a metatype mutation. Only the subsequent explicit confirmation
        // persists metatype + nationality together, through the Core authority.
        OriginDossierLifeModuleEffectState[] metatypes = _state.Choices
            .Select(MetatypeEffect).OfType<OriginDossierLifeModuleEffectState>()
            .GroupBy(effect => effect.TargetId, StringComparer.Ordinal)
            .Select(group => group.First()).OrderBy(effect => effect.AfterValue, StringComparer.Ordinal)
            .ToArray();
        if (metatypes.Length > 0)
        {
            body.Add(NativeTheme.Eyebrow(_copy["Origin.ChooseMetatype"]));
            body.Add(NativeTheme.Body(_copy["Origin.MetatypeReviewDetail"], NativeTheme.Muted));
            foreach (OriginDossierLifeModuleEffectState metatype in metatypes)
            {
                Button selectMetatype = _selectedMetatypeOptionId == metatype.TargetId
                    ? NativeTheme.PrimaryButton(metatype.AfterValue)
                    : NativeTheme.SecondaryButton(metatype.AfterValue);
                selectMetatype.AutomationId = "origin-life-metatype-" + metatype.TargetId;
                selectMetatype.Clicked += (_, _) =>
                {
                    if (_actionInFlight || generation != _renderGeneration)
                        return;
                    _selectedMetatypeOptionId = metatype.TargetId;
                    Content = new ScrollView { Content = BuildBody() };
                };
                body.Add(selectMetatype);
            }
            if (_selectedMetatypeOptionId is not null)
                body.Add(NativeTheme.Eyebrow(_copy["Origin.ChooseNationality"]));
        }

        // Keep stable automation identities while moving the selected review
        // ahead of compact alternatives. Confirmation must not require
        // scrolling through every other module's effects.
        foreach (int choiceIndex in Enumerable.Range(0, _state.Choices.Count)
                     .OrderByDescending(index => _state.Choices[index].ChoiceId == _editingChoiceId)
                     .ThenByDescending(index => _state.Choices[index].IsSelected)
                     .ThenByDescending(index => IsSelectionFinish(_state.Choices[index])))
        {
            OriginDossierLifeModuleChoiceState choice = _state.Choices[choiceIndex];
            if (metatypes.Length > 0 && MetatypeEffect(choice)?.TargetId != _selectedMetatypeOptionId)
                continue;
            var card = new VerticalStackLayout { Spacing = 8 };
            Button select = choice.IsSelected
                ? NativeTheme.PrimaryButton(choice.Label)
                : NativeTheme.SecondaryButton(choice.Label);
            select.AutomationId = $"origin-life-choice-{choiceIndex}";
            string choiceId = choice.ChoiceId;
            select.Clicked += async (_, _) =>
            {
                if (_actionInFlight || generation != _renderGeneration)
                    return;
                if (FollowUps(choiceId).Count > 0)
                {
                    BeginEditing(choiceId);
                    Content = new ScrollView { Content = BuildBody() };
                    return;
                }
                _editingChoiceId = null;
                _actionInFlight = true;
                select.IsEnabled = false;
                try
                {
                    OriginDossierLifeModulePhoneResult? prepared = await _prepareChoice(choiceId, null);
                    if (prepared is not null && generation == _renderGeneration && TryAdoptPrepared(prepared))
                        Content = new ScrollView { Content = BuildBody() };
                }
                finally { _actionInFlight = false; select.IsEnabled = true; }
            };
            card.Add(select);

            Label source = NativeTheme.Body(
                string.IsNullOrWhiteSpace(choice.PageReference)
                    ? choice.Source
                    : $"{choice.Source} · {choice.PageReference}",
                NativeTheme.Muted);
            source.AutomationId = $"origin-life-choice-source-{choiceIndex}";
            card.Add(source);
            card.Add(NativeTheme.Metric(_copy["Origin.Karma"], choice.KarmaRaw));
            if (choiceId == _editingChoiceId)
            {
                AddInputForm(card, choiceId, generation);
                body.Add(NativeTheme.Card(card));
                continue;
            }
            if (!choice.IsSelected || _editingChoiceId is not null)
            {
                body.Add(NativeTheme.Card(card));
                continue;
            }
            Label anchors = NativeTheme.Body(
                _copy.Format(
                    "Origin.SourceAnchors",
                    string.Join(", ", choice.SourceAnchorIds)),
                NativeTheme.Muted);
            anchors.AutomationId = $"origin-life-choice-anchors-{choiceIndex}";
            card.Add(anchors);
            if (_storyCheckpoint?.PendingPreview?.InputResolution is { } answers)
                foreach (var question in FollowUps(choiceId))
                    if (answers.Values.TryGetValue(question.PromptId, out string? answer))
                        card.Add(NativeTheme.Body((question.DisplayLabel ?? question.Label) + ": " + answer));

            // Historic mechanics rows are raw source projections, not ratings.
            // Only display the fresh compiler review. Do not repair old digests
            // or infer numeric defaults/quality identities in the Android host.
            var review = _storyCheckpoint?.PendingPreview?.EffectReview;
            card.Add(NativeTheme.Body(_copy[review is null
                ? "Origin.EffectReviewRequired" : "Origin.ContributionScope"], NativeTheme.Muted));
            for (int effectIndex = 0; effectIndex < (review?.Contributions.Count ?? 0); effectIndex++)
            {
                var effect = review!.Contributions[effectIndex];
                Label effectLabel = NativeTheme.Body(ContributionText(effect));
                effectLabel.AutomationId = $"origin-life-effect-{choiceIndex}-{effectIndex}";
                card.Add(effectLabel);
                if (effect.DescriptiveMetadata.Count > 0)
                    card.Add(NativeTheme.Body(_copy.Format("Origin.EffectSourceContext",
                        string.Join(" · ", effect.DescriptiveMetadata.OrderBy(item => item.Key, StringComparer.Ordinal)
                            .Select(item => item.Value))), NativeTheme.Muted));
            }
            body.Add(NativeTheme.Card(card));
            AddConfirmation(body, generation);
        }

        Label provenance = NativeTheme.Body(
            _copy.Format("Origin.NarrativeOnly", _state.LtdProviderDisplay),
            NativeTheme.Muted);
        provenance.AutomationId = "origin-life-ltd-provenance";
        body.Add(provenance);

        return body;
    }

    private string ContributionText(LifeModuleEffectContribution effect)
    {
        if (effect.Kind == "pushtext" && !string.IsNullOrEmpty(effect.SelectionText))
            return _copy.Format("Origin.SelectionContribution", effect.SelectionText);
        if (effect.CompilationStatus != CharacterCreationFoundationEffectCompilationStatuses.Supported)
            return _copy.Format("Origin.EffectPending", effect.TargetName);
        string amount = effect.Amount?.ToString("+0.################;-0.################;0",
            CultureInfo.GetCultureInfo(_locale.FormattingLocale)) ?? string.Empty;
        return effect.Kind switch
        {
            "attributelevel" => _copy.Format("Origin.AttributeContribution", effect.TargetName, amount),
            "skilllevel" => _copy.Format("Origin.SkillContribution", effect.TargetName, amount),
            "skillgrouplevel" => _copy.Format("Origin.GroupContribution", effect.TargetName, amount),
            "knowledgeskilllevel" => _copy.Format("Origin.KnowledgePoolContribution", amount),
            "freepositivequalities" => _copy.Format("Origin.PositivePoolContribution", amount),
            "freenegativequalities" => _copy.Format("Origin.NegativePoolContribution", amount),
            "qualitylevel" => _copy.Format("Origin.QualityLevelContribution", effect.TargetName,
                effect.Amount?.ToString(CultureInfo.GetCultureInfo(_locale.FormattingLocale)) ?? string.Empty),
            "addqualities" => _copy.Format("Origin.QualityContribution", effect.TargetName, effect.SelectionText),
            _ => _copy.Format("Origin.EffectPending", effect.TargetName)
        };
    }

    private IReadOnlyList<LifeModuleFollowUpPromptDto> FollowUps(string choiceId)
        => _storyCheckpoint?.Projection.CurrentTurn.LegalChoices
            .SingleOrDefault(choice => choice.ChoiceId == choiceId)?.FollowUps ?? [];

    private void BeginEditing(string choiceId)
    {
        _editingChoiceId = choiceId;
        _answers.Clear();
        if (_storyCheckpoint?.PendingPreview?.InputResolution is { } pending && pending.ChoiceId == choiceId)
            foreach (var answer in pending.Values)
                _answers.Add(answer.Key, answer.Value);
    }

    private void AddInputForm(VerticalStackLayout card, string choiceId, int generation)
    {
        var prompts = FollowUps(choiceId);
        var review = NativeTheme.PrimaryButton(_copy["Origin.ReviewAnswers"]);
        review.AutomationId = "origin-life-review-answers";
        void UpdateReady() => review.IsEnabled = !_actionInFlight && prompts.All(prompt => !prompt.IsRequired
            || _answers.TryGetValue(prompt.PromptId, out var answer) && !string.IsNullOrWhiteSpace(answer));
        card.Add(NativeTheme.Body(_copy["Origin.AnswersDetail"], NativeTheme.Muted));
        foreach (var prompt in prompts)
        {
            card.Add(NativeTheme.Body((prompt.DisplayLabel ?? prompt.Label) + (prompt.IsRequired ? " *" : string.Empty)));
            string promptId = prompt.PromptId;
            _answers.TryGetValue(promptId, out var previous);
            if (prompt.InputKind == "single-select")
            {
                var options = prompt.Options.Where(option => option.IsEnabled).ToArray();
                var picker = new Picker
                {
                    Title = _copy["Origin.ChooseAnswer"],
                    AutomationId = "origin-life-answer-" + promptId,
                    ItemsSource = options,
                    ItemDisplayBinding = new Binding(nameof(LifeModuleFollowUpOptionDto.Label)),
                    SelectedIndex = Array.FindIndex(options, option => option.SourceValue == previous)
                };
                picker.SelectedIndexChanged += (_, _) =>
                {
                    if (_actionInFlight || generation != _renderGeneration) return;
                    if (picker.SelectedIndex >= 0 && picker.SelectedIndex < options.Length)
                        _answers[promptId] = options[picker.SelectedIndex].SourceValue;
                    else _answers.Remove(promptId);
                    UpdateReady();
                };
                card.Add(picker);
            }
            else
            {
                var entry = new Entry
                {
                    AutomationId = "origin-life-answer-" + promptId,
                    Text = previous, MaxLength = 1024, Placeholder = _copy["Origin.EnterAnswer"]
                };
                entry.TextChanged += (_, _) =>
                {
                    if (_actionInFlight || generation != _renderGeneration) return;
                    _answers[promptId] = entry.Text?.Trim() ?? string.Empty;
                    UpdateReady();
                };
                card.Add(entry);
            }
        }
        UpdateReady();
        review.Clicked += async (_, _) =>
        {
            if (_actionInFlight || generation != _renderGeneration) return;
            _actionInFlight = true;
            review.IsEnabled = false;
            try
            {
                var answers = new Dictionary<string, string>(_answers, StringComparer.Ordinal);
                var prepared = await _prepareChoice(choiceId, answers);
                if (generation == _renderGeneration && prepared is not null && TryAdoptPrepared(prepared))
                {
                    _editingChoiceId = null;
                    Content = new ScrollView { Content = BuildBody() };
                }
            }
            finally { _actionInFlight = false; UpdateReady(); }
        };
        card.Add(review);
    }

    // Only called after rendering the selected, metatype-filtered card and its
    // exact Core preview. Changing metatype hides the old preview and action.
    private void AddConfirmation(VerticalStackLayout body, int generation)
    {
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
            confirm.IsEnabled = CanConfirmReviewed;
            var progress = new ActivityIndicator
            {
                AutomationId = "origin-life-saving", IsVisible = false,
                WidthRequest = 24, HeightRequest = 24,
                HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center
            };
            // Reserve space so showing feedback does not move the tapped action.
            var confirmationRow = new Grid
            {
                HeightRequest = confirm.HeightRequest, ColumnSpacing = 10,
                ColumnDefinitions = { new(GridLength.Star), new(new GridLength(32)) }
            };
            confirmationRow.Add(confirm, 0);
            confirmationRow.Add(progress, 1);
            confirm.Clicked += async (_, _) =>
            {
                if (_actionInFlight || generation != _renderGeneration || !CanConfirmReviewed)
                    return;
                _actionInFlight = true;
                confirm.IsEnabled = false;
                body.IsEnabled = false;
                confirm.Text = _copy["Origin.SavingDecision"];
                progress.IsVisible = progress.IsRunning = true;
                try
                {
                    var confirmed = await _confirmChoice(selectedChoiceId, previewDigest);
                    if (generation != _renderGeneration || confirmed?.IsSuccess != true)
                        return;
                    if (confirmed.Completed)
                        await Navigation.PopAsync();
                    else if (TryAdoptConfirmed(confirmed))
                        Content = new ScrollView { Content = BuildBody() };
                }
                finally
                {
                    _actionInFlight = false;
                    progress.IsRunning = progress.IsVisible = false;
                    body.IsEnabled = true;
                    confirm.Text = _copy["Origin.Confirm"];
                    confirm.IsEnabled = generation == _renderGeneration && CanConfirmReviewed;
                }
            };
            body.Add(confirmationRow);
        }

    }

    private bool CanConfirmReviewed => _state.CanConfirm && _storyCheckpoint?.PendingPreview?.EffectReview is not null;

    private static OriginDossierLifeModuleEffectState? MetatypeEffect(OriginDossierLifeModuleChoiceState choice)
        => choice.Effects.SingleOrDefault(effect => effect.Domain == "metatype-choice");

    // Core determines whether finishing is available. Only promote an already
    // admitted typed action; never infer permission from stage, cost or label.
    private static bool IsSelectionFinish(OriginDossierLifeModuleChoiceState choice)
        => choice.Effects.Any(effect => effect.Domain == "creation-stage"
            && effect.TargetId == CharacterCreationLifeModuleStageIds.SelectionFinished
            && effect.AfterValue == "true");

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
        _storyCheckpoint = prepared.StoryCheckpoint;
        return true;
    }

    private bool TryAdoptConfirmed(OriginDossierLifeModulePhoneResult confirmed)
    {
        if (!TryReadDisplayAuthority(confirmed, out var state, out var budget)
            || confirmed.StoryCheckpoint is not { } checkpoint
            || state.WorkspaceId != _state.WorkspaceId || state.OwnerId != _state.OwnerId
            || state.Locale != _state.Locale || state.WorkspaceRevision != _state.WorkspaceRevision + 1
            || state.TurnSequence != _state.TurnSequence + 1 || state.StageOrder < _state.StageOrder
            || state.CanConfirm || checkpoint.PendingPreview is not null
            || checkpoint.Projection.CurrentTurn.PreviousTurnDigest != _state.BoundTurnSeedDigest
            || confirmed.BoundSourceDigest != _boundSourceDigest
            || budget.Total != _budget.Total || budget.Unit != _budget.Unit)
            return false;

        _state = state;
        _budget = budget;
        _storyCheckpoint = checkpoint;
        _foundationSnapshotDigest = confirmed.FoundationSnapshotDigest!;
        _boundContentDigest = confirmed.BoundContentDigest!;
        _boundSourceDigest = confirmed.BoundSourceDigest!;
        _boundMechanicsSnapshotDigest = confirmed.BoundMechanicsSnapshotDigest!;
        _selectedMetatypeOptionId = null;
        _editingChoiceId = null;
        _answers.Clear();
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
