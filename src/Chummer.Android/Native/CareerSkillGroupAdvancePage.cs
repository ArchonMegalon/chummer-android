using System.Globalization;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class CareerSkillGroupAdvancePage : NativePageBase
{
    private readonly CareerSkillGroupAdvanceEditorState _editor;
    private readonly Picker _groups;
    private readonly Label _rating;
    private readonly Label _cost;
    private readonly Label _blocker;
    private readonly Button _advance;
    private CharacterCareerSkillGroupAdvanceQuote? _selected;

    public CareerSkillGroupAdvancePage(
        RunnerSessionCoordinator coordinator,
        CareerSkillGroupAdvanceEditorState editor) : base(coordinator)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _selected = editor.SkillGroups.FirstOrDefault();
        Title = "Advance skill group";
        AutomationId = "career-skill-group-page";

        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow("Career runner"));
        body.Add(NativeTheme.Title("Advance a skill group"));
        body.Add(NativeTheme.Body(
            "Choose one saved Chummer5 skill group. Member ratings, category modifiers, maximum, Karma, rule digest and undo identity are revalidated atomically when you confirm.",
            NativeTheme.Muted));

        body.Add(NativeTheme.FieldLabel("Skill group"));
        _groups = new Picker
        {
            AutomationId = "career-skill-group-picker",
            Title = "Saved skill group",
            ItemsSource = editor.SkillGroups.Select(GroupLabel).ToArray(),
            SelectedIndex = editor.SkillGroups.Count > 0 ? 0 : -1,
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        _groups.SelectedIndexChanged += (_, _) => SelectGroup();
        body.Add(_groups);

        _rating = NativeTheme.Body(string.Empty, NativeTheme.Text);
        _rating.AutomationId = "career-skill-group-rating";
        body.Add(_rating);
        _cost = NativeTheme.Body(string.Empty, NativeTheme.Text);
        _cost.AutomationId = "career-skill-group-cost";
        body.Add(_cost);
        _blocker = NativeTheme.Body(string.Empty, NativeTheme.Danger);
        _blocker.AutomationId = "career-skill-group-blocker";
        body.Add(_blocker);

        if (editor.OmittedSkillGroupCount > 0)
        {
            Label omitted = NativeTheme.Body(
                $"{editor.OmittedSkillGroupCount.ToString(CultureInfo.InvariantCulture)} group(s) are hidden because their exact source, member-rating, movement, or Improvement authority cannot be reproduced safely.",
                NativeTheme.Danger);
            omitted.AutomationId = "career-skill-group-omitted";
            body.Add(NativeTheme.Card(omitted));
        }

        _advance = NativeTheme.PrimaryButton("Spend Karma and advance group");
        _advance.AutomationId = "career-skill-group-advance";
        _advance.Clicked += async (_, _) => await RunAsync(AdvanceAsync);
        body.Add(_advance);

        Content = new ScrollView { Content = body };
        RefreshEnabledState();
    }

    protected override void Refresh() => RefreshEnabledState();

    private static string GroupLabel(CharacterCareerSkillGroupAdvanceQuote group)
        => $"{group.Name} · {group.Rating.ToString(CultureInfo.InvariantCulture)} → "
            + $"{(group.Rating + 1).ToString(CultureInfo.InvariantCulture)} · "
            + $"{group.KarmaCost.ToString(CultureInfo.InvariantCulture)} Karma · {group.Identity.SkillGroupId:D}";

    private void SelectGroup()
    {
        _selected = _groups.SelectedIndex >= 0 && _groups.SelectedIndex < _editor.SkillGroups.Count
            ? _editor.SkillGroups[_groups.SelectedIndex]
            : null;
        RefreshEnabledState();
    }

    private void RefreshEnabledState()
    {
        bool revisionMatches = Coordinator.State.WorkspaceId == _editor.WorkspaceId
            && Coordinator.State.ContentRevision == _editor.ContentRevision;
        _groups.IsEnabled = revisionMatches && _editor.SkillGroups.Count > 0;
        _rating.Text = _selected is null
            ? "No exact skill-group advancement is currently available."
            : $"Current rating {_selected.Rating.ToString(CultureInfo.InvariantCulture)} · maximum {_selected.RatingMaximum.ToString(CultureInfo.InvariantCulture)}";
        _cost.Text = _selected is null
            ? string.Empty
            : $"Cost {_selected.KarmaCost.ToString(CultureInfo.InvariantCulture)} Karma · available {_selected.AvailableKarma.ToString(CultureInfo.InvariantCulture)}";
        _blocker.Text = !revisionMatches
            ? "This runner changed. Reopen skill-group advancement."
            : _selected?.Blocker switch
            {
                CharacterCareerSkillGroupAdvanceBlocker.Broken =>
                    "This skill group is broken and cannot be advanced as a group.",
                CharacterCareerSkillGroupAdvanceBlocker.Disabled =>
                    "This skill group is disabled by the runner's exact Improvements.",
                CharacterCareerSkillGroupAdvanceBlocker.AtMaximum =>
                    "This skill group is already at its exact career maximum.",
                CharacterCareerSkillGroupAdvanceBlocker.InsufficientKarma =>
                    "The runner does not have enough Karma for this group advancement.",
                _ => string.Empty
            };
        _advance.IsEnabled = revisionMatches
            && _selected is { CanAdvance: true }
            && CharacterCareerSkillGroupAdvanceRules.IsCoherent(_selected);
    }

    private async Task AdvanceAsync()
    {
        CharacterCareerSkillGroupAdvanceQuote? selected = _selected;
        if (selected is null
            || !selected.CanAdvance
            || !CharacterCareerSkillGroupAdvanceRules.IsCoherent(selected))
        {
            await DisplayAlertAsync(
                "Skill group cannot advance",
                "Reopen the runner and choose an unbroken, enabled group with exact rule authority and sufficient Karma.",
                "OK");
            return;
        }

        bool confirmed = await DisplayAlertAsync(
            "Spend Karma?",
            $"Advance {selected.Name} from {selected.Rating.ToString(CultureInfo.InvariantCulture)} to {(selected.Rating + 1).ToString(CultureInfo.InvariantCulture)} for {selected.KarmaCost.ToString(CultureInfo.InvariantCulture)} Karma?",
            "Advance group",
            "Cancel");
        if (!confirmed)
        {
            return;
        }

        bool persisted = await Coordinator.ApplyCareerSkillGroupAdvanceAsync(
            new CareerSkillGroupAdvanceRequest(
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
