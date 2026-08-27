using Chummer.Presentation.OriginBooks;

namespace Chummer.Android.Native;

/// <summary>
/// Phone rendering for a Core-validated live Life Module decision. Callers own
/// prepare/confirm orchestration; this page never calculates or applies an
/// effect itself.
/// </summary>
internal sealed class OriginDossierLifeModuleDecisionPage : ContentPage
{
    private readonly OriginDossierLifeModuleDecisionState _state;
    private readonly OriginDossierNarrativeLocaleBinding _locale;
    private readonly Func<string, Task> _prepareChoice;
    private readonly Func<string, string, Task> _confirmChoice;

    public OriginDossierLifeModuleDecisionPage(
        OriginDossierLifeModuleDecisionState state,
        string activeAppLocale,
        Func<string, Task> prepareChoice,
        Func<string, string, Task> confirmChoice)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        OriginDossierNarrativeLocaleBinding locale =
            OriginDossierNarrativeLocalePolicy.Resolve(activeAppLocale);
        if (!locale.CanRenderNarrativeLocale(_state.Locale)
            || string.IsNullOrWhiteSpace(_state.BoundTurnSeedDigest))
        {
            throw new InvalidOperationException(
                "The Origin Dossier decision is not bound to the active app language.");
        }
        _locale = locale;
        _prepareChoice = prepareChoice ?? throw new ArgumentNullException(nameof(prepareChoice));
        _confirmChoice = confirmChoice ?? throw new ArgumentNullException(nameof(confirmChoice));
        Title = "Life Modules";
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
        body.Add(NativeTheme.Eyebrow($"Stage {_state.StageOrder} · turn {_state.TurnSequence}"));
        body.Add(NativeTheme.Title(_state.RunnerDisplayName));
        string localeCopy = _locale.UsesEnglishFallback
            ? $"Story language · English fallback · formatting {_locale.FormattingLocale}"
            : $"Story language · {_locale.ResourceLanguage.ToUpperInvariant()} · formatting {_locale.FormattingLocale}";
        Label locale = NativeTheme.Body(localeCopy, NativeTheme.Muted);
        locale.AutomationId = "origin-life-locale";
        SemanticProperties.SetDescription(
            locale,
            $"Origin story resource language {_locale.ResourceLanguage}; formatting locale {_locale.FormattingLocale}; "
            + $"English fallback {_locale.UsesEnglishFallback}");
        body.Add(locale);

        Label story = NativeTheme.Body(_state.VisibleStoryMarkdown);
        story.AutomationId = "origin-life-story";
        SemanticProperties.SetDescription(
            story,
            $"Origin story in {_state.Locale}, through the current decision point");
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
            select.Clicked += async (_, _) => await _prepareChoice(choiceId);
            card.Add(select);

            Label source = NativeTheme.Body(
                string.IsNullOrWhiteSpace(choice.PageReference)
                    ? choice.Source
                    : $"{choice.Source} · {choice.PageReference}",
                NativeTheme.Muted);
            source.AutomationId = $"origin-life-choice-source-{choiceIndex}";
            card.Add(source);
            card.Add(NativeTheme.Metric("Karma", choice.KarmaRaw));

            for (int effectIndex = 0; effectIndex < choice.Effects.Count; effectIndex++)
            {
                OriginDossierLifeModuleEffectState effect = choice.Effects[effectIndex];
                Label effectLabel = NativeTheme.Body(
                    $"{RunnerSessionCoordinator.HumanizeId(effect.Domain)} · "
                    + $"{RunnerSessionCoordinator.HumanizeId(effect.TargetId)}: "
                    + $"{effect.BeforeValue} → {effect.AfterValue}");
                effectLabel.AutomationId = $"origin-life-effect-{choiceIndex}-{effectIndex}";
                card.Add(effectLabel);
            }
            body.Add(NativeTheme.Card(card));
        }

        Label provenance = NativeTheme.Body(
            _state.LtdProviderDisplay + " · narrative only; mechanics unchanged",
            NativeTheme.Muted);
        provenance.AutomationId = "origin-life-ltd-provenance";
        body.Add(provenance);

        if (_state.SelectedChoiceId is { } selectedChoiceId
            && _state.PendingPreviewDigest is { } previewDigest)
        {
            Label preview = NativeTheme.Body(
                "Review the exact source and effects above before confirming.",
                NativeTheme.Ink);
            preview.AutomationId = "origin-life-preview";
            body.Add(preview);
            Button confirm = NativeTheme.PrimaryButton("Confirm this decision");
            confirm.AutomationId = "origin-life-confirm";
            confirm.IsEnabled = _state.CanConfirm;
            confirm.Clicked += async (_, _) =>
                await _confirmChoice(selectedChoiceId, previewDigest);
            body.Add(confirm);
        }

        return body;
    }
}
