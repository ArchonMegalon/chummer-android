using System.Globalization;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

/// <summary>
/// Phone-only quote, review, and explicit-confirm surface for an exact Chummer5 Career
/// specialization purchase. All eligibility, cost, digest, and mutation authority remains in
/// Core and Presentation.
/// </summary>
public sealed class CareerSkillSpecializationPage : NativePageBase
{
    private readonly CareerSkillSpecializationEditorState _editor;
    private readonly Picker _skills;
    private readonly Picker _options;
    private readonly Entry _customName;
    private readonly Label _identity;
    private readonly Label _rating;
    private readonly Label _selectionOrigin;
    private readonly Label _quoteSummary;
    private readonly Label _groupConsequence;
    private readonly Label _blocker;
    private readonly Button _review;
    private CareerSkillSpecializationCandidate? _selectedSkill;
    private IReadOnlyList<CharacterCareerSkillSpecializationOption> _availableOptions = [];

    public CareerSkillSpecializationPage(
        RunnerSessionCoordinator coordinator,
        CareerSkillSpecializationEditorState editor) : base(coordinator)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _selectedSkill = editor.Skills.FirstOrDefault();
        Title = "Buy specialization";
        AutomationId = "career-skill-specialization-page";

        ToolbarItems.Add(new ToolbarItem
        {
            Text = "Rook",
            AutomationId = "career-skill-specialization-rook",
            Command = new Command(
                async () => await Navigation.PushAsync(new RookConversationPage(Coordinator)))
        });

        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow("Career runner"));
        body.Add(NativeTheme.Title("Buy a skill specialization"));
        body.Add(NativeTheme.Body(
            "Choose the exact saved active or knowledge skill, then choose a source-backed, "
            + "weapon, Improvement, or custom specialization. Review creates a fresh "
            + "revision-bound quote; only the separate confirmation may spend Karma.",
            NativeTheme.Muted));

        body.Add(NativeTheme.FieldLabel("Saved parent skill"));
        _skills = new Picker
        {
            AutomationId = "career-skill-specialization-skill-picker",
            Title = "Saved active or knowledge skill",
            ItemsSource = editor.Skills.Select(SkillLabel).ToArray(),
            SelectedIndex = editor.Skills.Count > 0 ? 0 : -1,
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        _skills.SelectedIndexChanged += (_, _) => SelectSkill();
        body.Add(_skills);

        _identity = NativeTheme.Body(string.Empty, NativeTheme.Muted);
        _identity.AutomationId = "career-skill-specialization-identity";
        body.Add(_identity);
        _rating = NativeTheme.Body(string.Empty, NativeTheme.Text);
        _rating.AutomationId = "career-skill-specialization-rating";
        body.Add(_rating);

        body.Add(NativeTheme.FieldLabel("Specialization"));
        _options = new Picker
        {
            AutomationId = "career-skill-specialization-option-picker",
            Title = "Exact specialization origin",
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        _options.SelectedIndexChanged += (_, _) => SelectOption();
        body.Add(_options);

        _customName = NativeTheme.TextField(
            "career-skill-specialization-custom-name",
            string.Empty,
            "Custom specialization name");
        _customName.TextChanged += (_, _) => SelectionChanged();
        body.Add(_customName);

        _selectionOrigin = NativeTheme.Body(string.Empty, NativeTheme.Muted);
        _selectionOrigin.AutomationId = "career-skill-specialization-selection-origin";
        body.Add(_selectionOrigin);

        _quoteSummary = NativeTheme.Body("Review a fresh quote before confirming.", NativeTheme.Text);
        _quoteSummary.AutomationId = "career-skill-specialization-quote";
        body.Add(_quoteSummary);
        _groupConsequence = NativeTheme.Body(string.Empty, NativeTheme.Muted);
        _groupConsequence.AutomationId = "career-skill-specialization-group-consequence";
        body.Add(_groupConsequence);
        _blocker = NativeTheme.Body(string.Empty, NativeTheme.Danger);
        _blocker.AutomationId = "career-skill-specialization-blocker";
        body.Add(_blocker);

        if (editor.OmittedSkillCount > 0)
        {
            Label omitted = NativeTheme.Body(
                $"{editor.OmittedSkillCount.ToString(CultureInfo.InvariantCulture)} skill(s) "
                + "are hidden because their exact source, rating, group, Improvement, or "
                + "custom-knowledge authority cannot be reproduced safely.",
                NativeTheme.Danger);
            omitted.AutomationId = "career-skill-specialization-omitted";
            body.Add(NativeTheme.Card(omitted));
        }

        _review = NativeTheme.PrimaryButton("Review specialization quote");
        _review.AutomationId = "career-skill-specialization-review";
        _review.Clicked += async (_, _) => await RunAsync(ReviewAsync);
        body.Add(_review);

        body.Add(NativeTheme.NavigationRow(
            "Ask Rook",
            "Ask a follow-up about the current revision without confirming this purchase",
            () => Navigation.PushAsync(new RookConversationPage(Coordinator)),
            automationId: "career-skill-specialization-rook-entry"));

        Content = new ScrollView { Content = body };
        BindOptions();
        RefreshEnabledState();
    }

    protected override void Refresh() => RefreshEnabledState();

    private static string SkillLabel(CareerSkillSpecializationCandidate skill)
    {
        string kind = skill.Identity.Kind switch
        {
            CharacterCareerSkillKind.Active => "Active",
            CharacterCareerSkillKind.Knowledge => "Knowledge",
            _ => "Unsupported"
        };
        return $"{skill.SkillName} · {kind} · rating "
            + $"{skill.TotalBaseRating.ToString(CultureInfo.InvariantCulture)} · "
            + $"{skill.Identity.SkillId:D}";
    }

    private static string OptionLabel(CharacterCareerSkillSpecializationOption option)
        => $"{option.Name} · {option.Kind} · {option.SourceAnchor}";

    private void SelectSkill()
    {
        _selectedSkill = _skills.SelectedIndex >= 0 && _skills.SelectedIndex < _editor.Skills.Count
            ? _editor.Skills[_skills.SelectedIndex]
            : null;
        BindOptions();
        SelectionChanged();
    }

    private void BindOptions()
    {
        _availableOptions = _selectedSkill?.AvailableOptions ?? [];
        string[] labels = _availableOptions
            .Select(OptionLabel)
            .Append("Custom · explicit user-provided value")
            .ToArray();
        _options.ItemsSource = labels;
        _options.SelectedIndex = labels.Length > 0 ? 0 : -1;
        _customName.IsVisible = IsCustomSelection;
    }

    private void SelectOption()
    {
        _customName.IsVisible = IsCustomSelection;
        SelectionChanged();
    }

    private void SelectionChanged()
    {
        _quoteSummary.Text = "Review a fresh quote before confirming.";
        _groupConsequence.Text = string.Empty;
        _blocker.Text = string.Empty;
        RefreshEnabledState();
    }

    private bool IsCustomSelection => _options.SelectedIndex == _availableOptions.Count;

    private CharacterCareerSkillSpecializationSelection? CurrentSelection()
    {
        if (_options.SelectedIndex >= 0 && _options.SelectedIndex < _availableOptions.Count)
        {
            CharacterCareerSkillSpecializationOption option =
                _availableOptions[_options.SelectedIndex];
            return new CharacterCareerSkillSpecializationSelection(
                option.Name,
                option.Kind,
                option.OptionIdentity);
        }

        if (!IsCustomSelection)
        {
            return null;
        }

        string customName = (_customName.Text ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(customName)
            || customName.Length > CharacterCareerSkillSpecializationRules.MaximumNameLength
                ? null
                : new CharacterCareerSkillSpecializationSelection(
                    customName,
                    CharacterCareerSkillSpecializationOptionKind.Custom,
                    OptionIdentity: null);
    }

    private void RefreshEnabledState()
    {
        bool revisionMatches = Coordinator.State.WorkspaceId == _editor.WorkspaceId
            && Coordinator.State.ContentRevision == _editor.ContentRevision;
        _skills.IsEnabled = revisionMatches && _editor.Skills.Count > 0;
        _options.IsEnabled = revisionMatches && _selectedSkill is not null;
        _customName.IsEnabled = revisionMatches && IsCustomSelection;

        _identity.Text = _selectedSkill is null
            ? "No exact active- or knowledge-skill identity is currently available."
            : $"{_selectedSkill.Identity.Kind} · saved {_selectedSkill.Identity.SkillId:D} · "
                + (_selectedSkill.Identity.SourceSkillId is { } sourceId
                    ? $"source {sourceId:D}"
                    : "custom knowledge source identity");
        _rating.Text = _selectedSkill is null
            ? string.Empty
            : $"Rating {_selectedSkill.TotalBaseRating.ToString(CultureInfo.InvariantCulture)} · "
                + $"existing specializations {_selectedSkill.ExistingSpecializationCount.ToString(CultureInfo.InvariantCulture)}"
                + (string.IsNullOrWhiteSpace(_selectedSkill.SkillGroup)
                    ? string.Empty
                    : $" · group {_selectedSkill.SkillGroup}");

        if (_options.SelectedIndex >= 0 && _options.SelectedIndex < _availableOptions.Count)
        {
            CharacterCareerSkillSpecializationOption option =
                _availableOptions[_options.SelectedIndex];
            _selectionOrigin.Text =
                $"Origin {option.Kind} · {option.SourceAnchor} · {option.OptionIdentity}";
        }
        else
        {
            _selectionOrigin.Text = IsCustomSelection
                ? "Origin Custom · explicit user-provided value · no fabricated source identity"
                : "Choose one exact specialization origin.";
        }

        _blocker.Text = !revisionMatches
            ? "This runner changed. Discard this selection and reopen specialization purchase."
            : SelectionBlockerText();
        _review.IsEnabled = revisionMatches
            && _selectedSkill is not null
            && CurrentSelection() is not null;
    }

    private string SelectionBlockerText()
    {
        if (_selectedSkill is null)
        {
            return "No exact active- or knowledge-skill identity is available to specialize.";
        }
        if (_options.SelectedIndex < 0)
        {
            return "Choose one exact specialization origin.";
        }
        if (!IsCustomSelection)
        {
            return string.Empty;
        }

        string customName = (_customName.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(customName))
        {
            return "Enter a custom specialization name before requesting a quote.";
        }
        return customName.Length > CharacterCareerSkillSpecializationRules.MaximumNameLength
            ? $"Custom specialization names are limited to "
                + $"{CharacterCareerSkillSpecializationRules.MaximumNameLength.ToString(CultureInfo.InvariantCulture)} characters."
            : string.Empty;
    }

    private async Task ReviewAsync()
    {
        CareerSkillSpecializationCandidate? selectedSkill = _selectedSkill;
        CharacterCareerSkillSpecializationSelection? selection = CurrentSelection();
        if (selectedSkill is null || selection is null)
        {
            await DisplayAlertAsync(
                "Selection incomplete",
                "Choose an exact specialization option or enter a valid custom name.",
                "OK");
            return;
        }

        CharacterCareerSkillSpecializationQuote? quote =
            await Coordinator.PrepareCareerSkillSpecializationQuoteAsync(
                new CareerSkillSpecializationQuoteRequest(
                    _editor.WorkspaceId,
                    _editor.ContentRevision,
                    selectedSkill.Identity,
                    selection));
        if (quote is null
            || quote.Identity != selectedSkill.Identity
            || quote.Selection != selection
            || !CharacterCareerSkillSpecializationRules.IsCoherent(quote))
        {
            _blocker.Text =
                "The source, rules, or runner revision changed. Reopen and request a fresh quote.";
            await DisplayAlertAsync(
                "Fresh quote unavailable",
                _blocker.Text,
                "OK");
            return;
        }

        _quoteSummary.Text =
            $"Cost {quote.KarmaCost.ToString(CultureInfo.InvariantCulture)} Karma · "
            + $"available {quote.AvailableKarma.ToString(CultureInfo.InvariantCulture)} · "
            + $"after {(quote.AvailableKarma - quote.KarmaCost).ToString(CultureInfo.InvariantCulture)}";
        _groupConsequence.Text = quote.WillBreakSkillGroup
            ? $"Consequence: buying this specialization breaks skill group {quote.SkillGroup}."
            : "Consequence: no skill group is broken by this purchase.";
        _groupConsequence.TextColor = quote.WillBreakSkillGroup
            ? NativeTheme.Danger
            : NativeTheme.Muted;
        _blocker.Text = BlockerText(quote.Blocker);

        if (!quote.CanAdd)
        {
            await DisplayAlertAsync(
                "Specialization cannot be bought",
                _blocker.Text,
                "OK");
            return;
        }

        bool confirmed = await DisplayAlertAsync(
            "Spend Karma?",
            $"Learn {quote.SkillName} ({quote.Selection.Name}) for "
            + $"{quote.KarmaCost.ToString(CultureInfo.InvariantCulture)} Karma?\n\n"
            + _groupConsequence.Text,
            "Buy specialization",
            "Cancel");
        if (!confirmed)
        {
            return;
        }

        bool persisted = await Coordinator.ApplyCareerSkillSpecializationAsync(
            new CareerSkillSpecializationRequest(
                _editor.WorkspaceId,
                _editor.ContentRevision,
                quote,
                quote.CharacterRevision,
                quote.SourceRevision,
                quote.RuleDigest,
                quote.LogicalRevision,
                Confirmed: true,
                SpecializationId: Guid.NewGuid(),
                ExpenseId: Guid.NewGuid(),
                ExpenseDateLocal: DateTime.Now));
        if (persisted)
        {
            await Navigation.PopAsync();
        }
    }

    private static string BlockerText(CharacterCareerSkillSpecializationBlocker blocker)
        => blocker switch
        {
            CharacterCareerSkillSpecializationBlocker.None => string.Empty,
            CharacterCareerSkillSpecializationBlocker.NativeLanguage =>
                "Native-language knowledge skills cannot buy specializations.",
            CharacterCareerSkillSpecializationBlocker.UpgradeDisallowed =>
                "This knowledge skill does not allow career upgrades.",
            CharacterCareerSkillSpecializationBlocker.SkillDisabled =>
                "This skill is disabled by the runner's exact Improvements.",
            CharacterCareerSkillSpecializationBlocker.ExoticSkill =>
                "Exotic skills cannot buy a separate specialization.",
            CharacterCareerSkillSpecializationBlocker.KarmaLocked =>
                "This skill is locked against Karma purchases.",
            CharacterCareerSkillSpecializationBlocker.RatingRequired =>
                "A positive skill rating is required before buying a specialization.",
            CharacterCareerSkillSpecializationBlocker.SkillSpecializationsBlocked =>
                "Specializations are blocked for this exact skill.",
            CharacterCareerSkillSpecializationBlocker.SkillCategorySpecializationsBlocked =>
                "Specializations are blocked for this exact skill category.",
            CharacterCareerSkillSpecializationBlocker.InsufficientKarma =>
                "The runner does not have enough Karma for this specialization.",
            _ => "The exact specialization authority rejected this purchase."
        };
}
