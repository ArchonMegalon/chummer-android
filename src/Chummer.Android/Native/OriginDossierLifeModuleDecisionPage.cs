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
    private readonly OriginDossierNarrativeLocaleBinding _locale;
    private readonly AndroidSurfaceCopy _copy;
    private readonly Func<string, Task<OriginDossierLifeModuleDecisionState?>> _prepareChoice;
    private readonly Func<string, string, Task<bool>> _confirmChoice;

    public OriginDossierLifeModuleDecisionPage(
        OriginDossierLifeModuleDecisionState state,
        string activeAppLocale,
        Func<string, Task<OriginDossierLifeModuleDecisionState?>> prepareChoice,
        Func<string, string, Task<bool>> confirmChoice)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
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
                OriginDossierLifeModuleDecisionState? prepared =
                    await _prepareChoice(choiceId);
                if (prepared is not null)
                {
                    _state = prepared;
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
}
