using Chummer.Contracts.Characters;
using Chummer.Application.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

internal sealed class CreationSkillsPhoneDraft
{
    private readonly Dictionary<(string Kind, string Id), CharacterCreationSkillAllocation> _skills = [];
    private readonly Dictionary<string, CharacterCreationSkillGroupAllocation> _groups = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Kind, string Id), int> _skillMinimums = [];
    private readonly Dictionary<string, int> _groupMinimums = new(StringComparer.Ordinal);
    private readonly HashSet<(string Kind, string Id)> _availableSkills = [];
    private readonly HashSet<string> _availableGroups = new(StringComparer.Ordinal);
    private CharacterCreationSkillsBinding? _binding;
    private string? _snapshotDigest;
    private CharacterCreationSkillsPreview? _preview;
    private CharacterCreationSkillsState? _preparedState;
    private CharacterCreationSkillsService.DraftSession? _session;

    public bool HasPreparedState(CharacterCreationSkillsState state)
        => ReferenceEquals(_preparedState, state) && _session is not null;

    public void RetireSession() { _session = null; _preparedState = null; }

    // Only the private Core session can supply this local projection. It is not
    // registered in the coordinator's confirmation table, so it cannot be saved.
    public bool TryPreviewPrepared(CharacterCreationSkillsState state,
        IReadOnlyList<CharacterCreationSkillAllocation> skills,
        IReadOnlyList<CharacterCreationSkillGroupAllocation> groups, out IReadOnlyList<string> blockers)
    {
        blockers = [CharacterCreationSkillsBlockers.StaleWorkspaceRevision];
        if (!HasPreparedState(state)) return false;
        var result = _session!.Preview(skills, groups);
        blockers = result.Blockers;
        if (!CanStagePrepared(result) || result.Value is not { } preview) return false;
        Adopt(preview);
        if (blockers.SequenceEqual(new[] { CharacterCreationSkillsBlockers.DraftDuplicate })) blockers = [];
        return true;
    }

    // Scale affordances share the same Core evaluator and staging policy, but
    // never adopt a speculative rating or mint review/save authority.
    public bool CanSelectPrepared(CharacterCreationSkillsState state,
        IReadOnlyList<CharacterCreationSkillAllocation> skills,
        IReadOnlyList<CharacterCreationSkillGroupAllocation> groups)
        => HasPreparedState(state) && CanStagePrepared(_session!.Preview(skills, groups));

    public Func<bool> PrepareSelection(CharacterCreationSkillsState state,
        IReadOnlyList<CharacterCreationSkillAllocation> skills,
        IReadOnlyList<CharacterCreationSkillGroupAllocation> groups)
    {
        var session = HasPreparedState(state) ? _session : null;
        var copiedSkills = skills.ToArray();
        var copiedGroups = groups.ToArray();
        return () => session is not null && CanStagePrepared(session.Preview(copiedSkills, copiedGroups));
    }

    private static bool CanStagePrepared(CharacterCreationFoundationResult<CharacterCreationSkillsPreview> result)
        => result.Value is { } preview
            && (result.Outcome == CharacterCreationFoundationOutcomes.Success && preview.CanConfirm && result.Blockers.Count == 0
                || result.Outcome == CharacterCreationFoundationOutcomes.Blocked
                && (result.Blockers.SequenceEqual(new[] { CharacterCreationSkillsBlockers.NativeLanguageRequired })
                    || result.Blockers.SequenceEqual(new[] { CharacterCreationSkillsBlockers.DraftDuplicate })));

    public void Bind(CharacterCreationSkillsState state, CharacterOverviewState overview)
    {
        if (Matches(state, overview))
        {
            if (!HasPreparedState(state))
            {
                _session = CharacterCreationSkillsService.CreateDraftSession(state);
                _preparedState = state;
            }
            return;
        }
        _skills.Clear(); _groups.Clear(); _binding = null; _snapshotDigest = null; _preview = null;
        _skillMinimums.Clear(); _groupMinimums.Clear();
        _availableSkills.Clear(); _availableGroups.Clear();
        RetireSession();
        if (!CreationSkillsPhoneAuthority.IsReady(state, overview)) return;
        _binding = state.Binding; _snapshotDigest = state.SnapshotDigest;
        foreach (var skill in CreationSkillsPhoneAuthority.AvailableActiveSkills(state).Concat(state.Authority.KnowledgeSkills))
            _availableSkills.Add((skill.Kind, skill.SourceSkillId));
        foreach (var group in CreationSkillsPhoneAuthority.AvailableGroups(state))
            _availableGroups.Add(group.GroupId);
        // The initial Core snapshot already contains free Priority talent rows.
        // A previously saved Skills draft is not required to display them.
        foreach (CharacterCreationSkillProjection item in state.Skills)
        {
            _skills[(item.Kind, item.SourceSkillId)] = new(item.SourceSkillId, item.Kind,
                item.Rating, item.SpecializationOptionId, item.IsNativeLanguage);
            _skillMinimums[(item.Kind, item.SourceSkillId)] = item.GrantedRating;
        }
        foreach (CharacterCreationSkillGroupProjection item in state.SkillGroups)
        {
            _groups[item.GroupId] = new(item.GroupId, item.Rating);
            _groupMinimums[item.GroupId] = item.GrantedRating;
        }
        _session = CharacterCreationSkillsService.CreateDraftSession(state);
        _preparedState = state;
    }

    public bool Matches(CharacterCreationSkillsState state, CharacterOverviewState overview) =>
        _binding is not null && CreationSkillsPhoneAuthority.IsReady(state, overview)
        && CreationSkillsPhoneAuthority.BindingEquals(_binding, state.Binding)
        && CharacterCreationSkillsDigest.EqualsFixedTime(_snapshotDigest, state.SnapshotDigest);
    public IReadOnlyList<CharacterCreationSkillAllocation> Skills => _skills.Values
        .OrderBy(item => item.Kind, StringComparer.Ordinal).ThenBy(item => item.SourceSkillId, StringComparer.Ordinal).ToArray();
    public IReadOnlyList<CharacterCreationSkillGroupAllocation> Groups => _groups.Values
        .OrderBy(item => item.GroupId, StringComparer.Ordinal).ToArray();
    public CharacterCreationSkillsPreview? Preview => _preview;

    public int MinimumRating(CharacterCreationSkillCatalogEntry source) =>
        _skillMinimums.GetValueOrDefault((source.Kind, source.SourceSkillId));
    public int MinimumRating(CharacterCreationSkillGroupCatalogEntry source) =>
        _groupMinimums.GetValueOrDefault(source.GroupId);

    public IReadOnlyList<CharacterCreationSkillAllocation> WithSkill(
        CharacterCreationSkillCatalogEntry source, int delta, bool native = false)
    {
        if (!_availableSkills.Contains((source.Kind, source.SourceSkillId))) return Skills;
        _skills.TryGetValue((source.Kind, source.SourceSkillId), out CharacterCreationSkillAllocation? current);
        if (native)
        {
            if (!source.CanBeNativeLanguage)
                return Skills;
            return Skills.Where(item => item.Kind != source.Kind || item.SourceSkillId != source.SourceSkillId)
                .Append(new(source.SourceSkillId, source.Kind, null, null, true))
                .ToArray();
        }

        int next = Math.Max(MinimumRating(source), (current is { IsNativeLanguage: false }
            ? current.Rating.GetValueOrDefault()
            : 0) + delta);
        var retained = Skills.Where(item => item.Kind != source.Kind || item.SourceSkillId != source.SourceSkillId);
        // Native languages intentionally have a null rating. Filtering the whole
        // collection by Rating > 0 would erase them when any other row changes.
        return next == 0 ? retained.ToArray() : retained.Append(new(source.SourceSkillId, source.Kind, next,
                next > 0 && current is { IsNativeLanguage: false } ? current.SpecializationOptionId : null,
                false))
            .ToArray();
    }

    public IReadOnlyList<CharacterCreationSkillGroupAllocation> WithGroup(
        CharacterCreationSkillGroupCatalogEntry source, int delta)
    {
        if (!_availableGroups.Contains(source.GroupId)) return Groups;
        _groups.TryGetValue(source.GroupId, out CharacterCreationSkillGroupAllocation? current);
        int next = Math.Max(MinimumRating(source), (current?.Rating ?? 0) + delta);
        return Groups.Where(item => item.GroupId != source.GroupId)
            .Append(new(source.GroupId, next)).Where(item => item.Rating > 0).ToArray();
    }

    public IReadOnlyList<CharacterCreationSkillAllocation> WithSpecialization(
        CharacterCreationSkillCatalogEntry source,
        string optionId)
    {
        if (!_availableSkills.Contains((source.Kind, source.SourceSkillId))) return Skills;
        _skills.TryGetValue((source.Kind, source.SourceSkillId), out CharacterCreationSkillAllocation? current);
        if (current is null
            || current.IsNativeLanguage
            || current.Rating is not > 0
            || !source.Specializations.Any(option => string.Equals(
                option.OptionId,
                optionId,
                StringComparison.Ordinal)))
            return Skills;
        string? selected = string.Equals(current.SpecializationOptionId, optionId, StringComparison.Ordinal)
            ? null : optionId;
        return Skills.Where(item => item.Kind != source.Kind || item.SourceSkillId != source.SourceSkillId)
            .Append(current with { SpecializationOptionId = selected })
            .ToArray();
    }

    public bool TryAdopt(CharacterCreationSkillsState state, CharacterOverviewState overview,
        CharacterCreationFoundationResult<CharacterCreationSkillsPreview> result,
        IReadOnlyList<CharacterCreationSkillAllocation> skills,
        IReadOnlyList<CharacterCreationSkillGroupAllocation> groups)
    {
        if (!Matches(state, overview)
            || !CreationSkillsPhoneAuthority.CanStagePreview(state, overview, result, skills, groups)
            || result.Value is not { } preview) return false;
        Adopt(preview);
        return true;
    }

    private void Adopt(CharacterCreationSkillsPreview preview)
    {
        _skills.Clear(); _groups.Clear();
        foreach (CharacterCreationSkillProjection item in preview.Skills)
            _skills[(item.Kind, item.SourceSkillId)] = new(
                item.SourceSkillId,
                item.Kind,
                item.Rating,
                item.SpecializationOptionId,
                item.IsNativeLanguage);
        foreach (CharacterCreationSkillGroupProjection item in preview.SkillGroups)
            _groups[item.GroupId] = new(item.GroupId, item.Rating);
        _preview = preview;
    }
}
