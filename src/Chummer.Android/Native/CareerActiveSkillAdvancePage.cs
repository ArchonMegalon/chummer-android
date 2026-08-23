using System.Globalization;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class CareerActiveSkillAdvancePage : NativePageBase
{
    private readonly CareerActiveSkillAdvanceEditorState _editor;
    private readonly Picker _skills;
    private readonly Label _rating;
    private readonly Label _cost;
    private readonly Label _blocker;
    private readonly Button _advance;
    private CharacterCareerActiveSkillAdvanceQuote? _selected;

    public CareerActiveSkillAdvancePage(
        RunnerSessionCoordinator coordinator,
        CareerActiveSkillAdvanceEditorState editor) : base(coordinator)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _selected = editor.Skills.FirstOrDefault();
        Title = "Advance skill";
        AutomationId = "career-active-skill-page";

        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow("Career runner"));
        body.Add(NativeTheme.Title("Advance an active skill"));
        body.Add(NativeTheme.Body(
            "Choose one saved Chummer5 skill. Cost, maximum rating, Karma balance, rule digest and undo identity are revalidated atomically when you confirm.",
            NativeTheme.Muted));

        body.Add(NativeTheme.FieldLabel("Active skill"));
        _skills = new Picker
        {
            AutomationId = "career-active-skill-picker",
            Title = "Saved active skill",
            ItemsSource = editor.Skills.Select(SkillLabel).ToArray(),
            SelectedIndex = editor.Skills.Count > 0 ? 0 : -1,
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        _skills.SelectedIndexChanged += (_, _) => SelectSkill();
        body.Add(_skills);

        _rating = NativeTheme.Body(string.Empty, NativeTheme.Text);
        _rating.AutomationId = "career-active-skill-rating";
        body.Add(_rating);
        _cost = NativeTheme.Body(string.Empty, NativeTheme.Text);
        _cost.AutomationId = "career-active-skill-cost";
        body.Add(_cost);
        _blocker = NativeTheme.Body(string.Empty, NativeTheme.Danger);
        _blocker.AutomationId = "career-active-skill-blocker";
        body.Add(_blocker);

        if (editor.OmittedSkillCount > 0)
        {
            Label omitted = NativeTheme.Body(
                $"{editor.OmittedSkillCount.ToString(CultureInfo.InvariantCulture)} skill(s) are hidden because their exact source, rating Improvement, or custom group-compensation authority cannot be reproduced safely.",
                NativeTheme.Danger);
            omitted.AutomationId = "career-active-skill-omitted";
            body.Add(NativeTheme.Card(omitted));
        }

        _advance = NativeTheme.PrimaryButton("Spend Karma and advance");
        _advance.AutomationId = "career-active-skill-advance";
        _advance.Clicked += async (_, _) => await RunAsync(AdvanceAsync);
        body.Add(_advance);

        Content = new ScrollView { Content = body };
        RefreshEnabledState();
    }

    protected override void Refresh() => RefreshEnabledState();

    private static string SkillLabel(CharacterCareerActiveSkillAdvanceQuote skill)
        => $"{skill.Name} · {skill.TotalBaseRating.ToString(CultureInfo.InvariantCulture)} → "
            + $"{(skill.TotalBaseRating + 1).ToString(CultureInfo.InvariantCulture)} · "
            + $"{skill.KarmaCost.ToString(CultureInfo.InvariantCulture)} Karma · {skill.Identity.SkillId:D}";

    private void SelectSkill()
    {
        _selected = _skills.SelectedIndex >= 0 && _skills.SelectedIndex < _editor.Skills.Count
            ? _editor.Skills[_skills.SelectedIndex]
            : null;
        RefreshEnabledState();
    }

    private void RefreshEnabledState()
    {
        bool revisionMatches = Coordinator.State.WorkspaceId == _editor.WorkspaceId
            && Coordinator.State.ContentRevision == _editor.ContentRevision;
        _skills.IsEnabled = revisionMatches && _editor.Skills.Count > 0;
        _rating.Text = _selected is null
            ? "No exact active-skill advancement is currently available."
            : $"Current rating {_selected.TotalBaseRating.ToString(CultureInfo.InvariantCulture)} · maximum {_selected.RatingMaximum.ToString(CultureInfo.InvariantCulture)}";
        _cost.Text = _selected is null
            ? string.Empty
            : $"Cost {_selected.KarmaCost.ToString(CultureInfo.InvariantCulture)} Karma · available {_selected.AvailableKarma.ToString(CultureInfo.InvariantCulture)}";
        _blocker.Text = !revisionMatches
            ? "This runner changed. Reopen active-skill advancement."
            : _selected?.Blocker switch
            {
                CharacterCareerActiveSkillAdvanceBlocker.AtMaximum =>
                    "This skill is already at its exact career maximum.",
                CharacterCareerActiveSkillAdvanceBlocker.InsufficientKarma =>
                    "The runner does not have enough Karma for this advancement.",
                _ => string.Empty
            };
        _advance.IsEnabled = revisionMatches
            && _selected is { CanAdvance: true }
            && CharacterCareerActiveSkillAdvanceRules.IsCoherent(_selected);
    }

    private async Task AdvanceAsync()
    {
        CharacterCareerActiveSkillAdvanceQuote? selected = _selected;
        if (selected is null
            || !selected.CanAdvance
            || !CharacterCareerActiveSkillAdvanceRules.IsCoherent(selected))
        {
            await DisplayAlertAsync(
                "Skill cannot advance",
                "Reopen the runner and choose a skill with exact rule authority and sufficient Karma.",
                "OK");
            return;
        }

        bool confirmed = await DisplayAlertAsync(
            "Spend Karma?",
            $"Advance {selected.Name} from {selected.TotalBaseRating.ToString(CultureInfo.InvariantCulture)} to {(selected.TotalBaseRating + 1).ToString(CultureInfo.InvariantCulture)} for {selected.KarmaCost.ToString(CultureInfo.InvariantCulture)} Karma?",
            "Advance",
            "Cancel");
        if (!confirmed)
        {
            return;
        }

        bool persisted = await Coordinator.ApplyCareerActiveSkillAdvanceAsync(
            new CareerActiveSkillAdvanceRequest(
                _editor.WorkspaceId,
                _editor.ContentRevision,
                selected,
                selected.RuleDigest,
                Confirmed: true,
                ExpenseId: Guid.NewGuid(),
                ExpenseDateLocal: DateTime.Now));
        if (persisted)
        {
            await Navigation.PopAsync();
        }
    }
}
