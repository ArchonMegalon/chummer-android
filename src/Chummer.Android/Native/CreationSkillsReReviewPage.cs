using System.Globalization;
using Chummer.Contracts.Characters;

namespace Chummer.Android.Native;

/// <summary>Explicit comparison of historical choices and current Core rules.
/// This page does not migrate on load, resolve rules, or save partial repairs.</summary>
public sealed class CreationSkillsReReviewPage : NativePageBase
{
    private readonly CreationSkillsReReviewPhoneDraft _draft = new();
    private readonly VerticalStackLayout _body = new() { Padding = new Thickness(20), Spacing = 12 };
    private IReadOnlyList<string> _requestBlockers = [];
    private CreationSkillsPhoneConfirmResult? _confirmation;
    private long _generation;
    private bool _attached;

    internal CreationSkillsReReviewPage(RunnerSessionCoordinator coordinator,
        CharacterCreationSkillsReReviewState state) : base(coordinator)
    {
        _draft.Bind(state, coordinator.State);
        Title = Text("Title", "Review older Skills choices");
        AutomationId = "creation-skills-rereview-page";
        Content = new ScrollView { Content = _body };
        _body.IsEnabled = false;
    }

    protected override void OnAppearing()
    {
        _attached = true;
        _body.IsEnabled = true;
        base.OnAppearing();
    }

    protected override void OnDisappearing()
    {
        _attached = false;
        _body.IsEnabled = false;
        Interlocked.Increment(ref _generation);
        base.OnDisappearing();
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Title(Text("Title", "Review older Skills choices")));
        _body.Add(NativeTheme.Body(Text("Intro",
            "Your saved choices remain unchanged. Compare them with current rules, correct any conflicts, then explicitly confirm the complete proposal."), NativeTheme.Muted));
        if (_confirmation?.Receipt is { } receipt)
        {
            var saved = NativeTheme.Body(_confirmation.Blockers.Count == 0
                ? Text("Saved", "Review saved. Previous receipts remain in history.")
                : Text("SavedRefresh", "The save has a receipt. Reopen the runner to refresh; do not apply it again."));
            saved.AutomationId = "creation-skills-rereview-saved";
            _body.Add(saved);
            var receiptLabel = NativeTheme.Body(receipt.ReceiptDigest, NativeTheme.Muted);
            receiptLabel.AutomationId = "creation-skills-rereview-receipt";
            _body.Add(receiptLabel);
            AddExit();
            return;
        }
        if (_draft.State is not { } state || _draft.Preview is not { } preview
            || !CreationSkillsReReviewPhoneAuthority.Matches(state, Coordinator.State))
        {
            _body.Add(NativeTheme.Body(Text("Stale", "The runner context changed. Reopen this review; nothing was applied."), NativeTheme.Danger));
            AddExit();
            return;
        }
        var binding = NativeTheme.Body(Format("Binding", "Revision {0} · historical draft {1}",
            state.Binding.Current.ContentRevision, state.HistoricalDraft.DraftRevision), NativeTheme.Muted);
        binding.AutomationId = "creation-skills-rereview-binding";
        _body.Add(binding);
        foreach (var budget in new[] { preview.CurrentPreview.ActiveSkillPointBudget,
                     preview.CurrentPreview.SkillGroupPointBudget, preview.CurrentPreview.KnowledgeSkillPointBudget })
            _body.Add(NativeTheme.Body(Format("Budget", "{0}: {1} / {2} points; {3} left",
                budget.Label, budget.Used, budget.Total, budget.Remaining)));
        foreach (var change in preview.Changes) AddChange(state, change);
        AddCatalogChoice(state);
        foreach (string blocker in preview.CurrentPreview.Blockers.Concat(_requestBlockers).Distinct(StringComparer.Ordinal))
            _body.Add(NativeTheme.Body(blocker, NativeTheme.Danger));
        var confirm = NativeTheme.PrimaryButton(Text("Confirm", "Review and confirm these changes"));
        confirm.AutomationId = "creation-skills-rereview-confirm";
        confirm.IsEnabled = _attached && _requestBlockers.Count == 0 && _draft.CanConfirm(Coordinator.State);
        confirm.Clicked += async (_, _) => await RunAsync(async () =>
        {
            long generation = Volatile.Read(ref _generation);
            if (!_attached || !_draft.CanConfirm(Coordinator.State)) return;
            var reviewed = _draft.Preview!;
            var skills = _draft.Skills.ToArray();
            var groups = _draft.Groups.ToArray();
            bool accepted = await DisplayAlertAsync(Text("Title", "Review older Skills choices"),
                Text("ConfirmBody", "Apply exactly the comparison shown? Removed choices will leave the new draft, but previous decisions and receipts remain in history."),
                Text("Apply", "Apply reviewed choices"), Text("Cancel", "Cancel"));
            if (!accepted || !_attached || generation != Volatile.Read(ref _generation) || !_draft.CanConfirm(Coordinator.State)) return;
            // The coordinator reprojects against the current store before CAS.
            // Preserve an observed receipt even if the page leaves while saving.
            _confirmation = await Coordinator.ConfirmCreationSkillsReReviewAsync(reviewed, skills, groups, explicitlyReviewed: true);
            _requestBlockers = _confirmation.Blockers;
        });
        _body.Add(confirm);
        AddExit();
    }

    private void AddChange(CharacterCreationSkillsReReviewState state, CharacterCreationSkillsReReviewChange change)
    {
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Title(change.Name, 18));
        card.Add(NativeTheme.Body(Format("Comparison", "Saved: {0} ({1} points) → proposal: {2} ({3} points)",
            Rating(change.HistoricalRating, change.HistoricalNativeLanguage), change.HistoricalPointCost,
            change.Removed ? Text("Removed", "removed") : Rating(change.CandidateRating, change.CandidateNativeLanguage), change.CandidatePointCost)));
        if (change.HistoricalSpecializationOptionId is not null || change.CandidateSpecializationOptionId is not null)
            card.Add(NativeTheme.Body(Format("Specialization", "Specialization: {0} → {1}",
                SpecializationName(state, change, change.HistoricalSpecializationOptionId),
                SpecializationName(state, change, change.CandidateSpecializationOptionId)), NativeTheme.Muted));
        foreach (string blocker in change.Blockers) card.Add(NativeTheme.Body(blocker, NativeTheme.Danger));
        if (change.SourceAnchorIds.Count > 0)
            card.Add(NativeTheme.Body(Format("Sources", "Source anchors: {0}", string.Join(", ", change.SourceAnchorIds)), NativeTheme.Muted));
        HorizontalStackLayout controls = new() { Spacing = 8 };
        bool group = change.Kind == "skill-group";
        var skill = _draft.Skills.SingleOrDefault(row => row.Kind == change.Kind && row.SourceSkillId == change.SourceId);
        var selectedGroup = group ? _draft.Groups.SingleOrDefault(row => row.GroupId == change.SourceId) : null;
        if (skill is not null || selectedGroup is not null)
        {
            Button remove = NativeTheme.SecondaryButton(Text("Remove", "Remove from proposal"));
            remove.AutomationId = "creation-skills-rereview-remove-" + Token(change.SourceId);
            remove.Clicked += async (_, _) => await ProposeAsync(
                _draft.Skills.Where(row => group || row.Kind != change.Kind || row.SourceSkillId != change.SourceId).ToArray(),
                _draft.Groups.Where(row => !group || row.GroupId != change.SourceId).ToArray());
            controls.Add(remove);
            if (skill?.IsNativeLanguage != true)
                foreach (int delta in new[] { -1, 1 })
                {
                    var adjust = NativeTheme.SecondaryButton(delta > 0 ? "+" : "−");
                    adjust.AutomationId = $"creation-skills-rereview-{(delta > 0 ? "plus" : "minus")}-{Token(change.SourceId)}";
                    adjust.IsEnabled = delta > 0 || (skill?.Rating ?? selectedGroup?.Rating ?? 0) > 0;
                    adjust.Clicked += async (_, _) =>
                    {
                        int rating = Math.Max(0, (skill?.Rating ?? selectedGroup?.Rating ?? 0) + delta);
                        await ProposeAsync(group ? _draft.Skills : _draft.Skills.Select(row => row == skill
                                ? row with { Rating = rating, SpecializationOptionId = rating == 0 ? null : row.SpecializationOptionId } : row)
                            .Where(row => row.IsNativeLanguage || row.Rating > 0).ToArray(),
                            !group ? _draft.Groups : _draft.Groups.Select(row => row == selectedGroup ? row with { Rating = rating } : row)
                                .Where(row => row.Rating > 0).ToArray());
                    };
                    controls.Add(adjust);
                }
        }
        else
        {
            var restore = NativeTheme.SecondaryButton(Text("Restore", "Restore saved choice to proposal"));
            restore.AutomationId = "creation-skills-rereview-restore-" + Token(change.SourceId);
            restore.Clicked += async (_, _) => await ProposeAsync(
                _draft.Skills.Concat(state.HistoricalDraft.Allocations.Where(row => row.Kind == change.Kind && row.SourceSkillId == change.SourceId)).ToArray(),
                _draft.Groups.Concat(state.HistoricalDraft.GroupAllocations.Where(row => group && row.GroupId == change.SourceId)).ToArray());
            controls.Add(restore);
        }
        card.Add(controls);
        var source = state.CurrentState.Authority.ActiveSkills.Concat(state.CurrentState.Authority.KnowledgeSkills)
            .SingleOrDefault(row => row.Kind == change.Kind && row.SourceSkillId == change.SourceId);
        if (skill is not null && source is not null)
        {
            if (source.CanBeNativeLanguage)
            {
                var native = NativeTheme.SecondaryButton(skill.IsNativeLanguage
                    ? Text("RatedLanguage", "Use rated language") : Text("NativeLanguage", "Use native language"));
                native.Clicked += async (_, _) => await ProposeAsync(_draft.Skills.Select(row => row == skill
                    ? row with { Rating = skill.IsNativeLanguage ? 1 : null, IsNativeLanguage = !skill.IsNativeLanguage, SpecializationOptionId = null } : row).ToArray(), _draft.Groups);
                card.Add(native);
            }
            if (!skill.IsNativeLanguage && source.Specializations.Count > 0)
            {
                var options = new[] { Text("NoSpecialization", "No specialization") }.Concat(source.Specializations.Select(row => row.Name)).ToArray();
                var picker = new Picker { Title = Text("ChooseSpecialization", "Choose specialization"), ItemsSource = options };
                var choose = NativeTheme.SecondaryButton(Text("SetSpecialization", "Preview specialization"));
                choose.Clicked += async (_, _) =>
                {
                    int selected = picker.SelectedIndex;
                    if (selected < 0 || selected >= options.Length) return;
                    string? optionId = selected == 0 ? null : source.Specializations[selected - 1].OptionId;
                    await ProposeAsync(_draft.Skills.Select(row => row == skill ? row with { SpecializationOptionId = optionId } : row).ToArray(), _draft.Groups);
                };
                card.Add(picker); card.Add(choose);
            }
        }
        var border = NativeTheme.Card(card);
        border.AutomationId = "creation-skills-rereview-change-" + Token(change.SourceId);
        _body.Add(border);
    }

    private void AddCatalogChoice(CharacterCreationSkillsReReviewState state)
    {
        var skills = CreationSkillsPhoneAuthority.AvailableActiveSkills(state.CurrentState)
            .Concat(state.CurrentState.Authority.KnowledgeSkills)
            .Where(source => !_draft.Skills.Any(row => row.Kind == source.Kind && row.SourceSkillId == source.SourceSkillId)).ToArray();
        var groups = CreationSkillsPhoneAuthority.AvailableGroups(state.CurrentState)
            .Where(source => !_draft.Groups.Any(row => row.GroupId == source.GroupId)).ToArray();
        var options = skills.Select(row => row.Name + " · " + row.Category)
            .Concat(groups.Select(row => row.Name + " · " + Text("Group", "skill group"))).ToArray();
        var picker = new Picker { Title = Text("AddChoice", "Add a current catalog choice"), ItemsSource = options };
        picker.AutomationId = "creation-skills-rereview-catalog";
        var add = NativeTheme.SecondaryButton(Text("AddPreview", "Preview added choice"));
        add.IsEnabled = options.Length > 0;
        add.Clicked += async (_, _) =>
        {
            int index = picker.SelectedIndex;
            if (index < 0 || index >= options.Length) return;
            if (index < skills.Length)
            {
                var source = skills[index];
                await ProposeAsync(_draft.Skills.Append(new(source.SourceSkillId, source.Kind, 1, null, false)).ToArray(), _draft.Groups);
            }
            else await ProposeAsync(_draft.Skills, _draft.Groups.Append(new(groups[index - skills.Length].GroupId, 1)).ToArray());
        };
        _body.Add(picker); _body.Add(add);
    }

    private Task ProposeAsync(IReadOnlyList<CharacterCreationSkillAllocation> skills,
        IReadOnlyList<CharacterCreationSkillGroupAllocation> groups) => RunAsync(async () =>
    {
        if (!_attached || _draft.State is not { } state || _confirmation?.Receipt is not null) return;
        long generation = Volatile.Read(ref _generation);
        var selectedSkills = skills.ToArray();
        var selectedGroups = groups.ToArray();
        var result = await Task.Run(() => Coordinator.PreviewCreationSkillsReReview(state.Binding, selectedSkills, selectedGroups));
        if (!_attached || generation != Volatile.Read(ref _generation)) return;
        _requestBlockers = _draft.TryAdopt(Coordinator.State, result, selectedSkills, selectedGroups)
            ? [] : result.Blockers.Count > 0 ? result.Blockers : [CharacterCreationSkillsReReviewSchemas.Stale];
    });

    private void AddExit()
    {
        var back = NativeTheme.SecondaryButton(Text("Back", "Back without further changes"));
        back.AutomationId = "creation-skills-rereview-back";
        back.Clicked += async (_, _) => await RunAsync(async () => { if (_attached) await Navigation.PopAsync(); });
        _body.Add(back);
    }

    private static string SpecializationName(CharacterCreationSkillsReReviewState state,
        CharacterCreationSkillsReReviewChange change, string? id) => id is null ? Text("None", "none")
        : state.CurrentState.Authority.ActiveSkills.Concat(state.CurrentState.Authority.KnowledgeSkills)
            .SingleOrDefault(row => row.Kind == change.Kind && row.SourceSkillId == change.SourceId)?.Specializations
            .SingleOrDefault(row => row.OptionId == id)?.Name ?? id;
    private static string Rating(int? rating, bool native) => native ? Text("Native", "native")
        : rating?.ToString(CultureInfo.CurrentCulture) ?? Text("None", "none");
    private static string Token(string value) => new(value.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
    private static string Text(string key, string fallback) => CreationAllocationStrings.Get("SkillsReReview." + key, fallback);
    private static string Format(string key, string fallback, params object?[] args) => CreationAllocationStrings.Format("SkillsReReview." + key, fallback, args);
}
