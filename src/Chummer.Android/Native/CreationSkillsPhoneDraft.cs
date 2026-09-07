using Chummer.Contracts.Characters;
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

    public void Bind(CharacterCreationSkillsState state, CharacterOverviewState overview)
    {
        if (Matches(state, overview)) return;
        _skills.Clear(); _groups.Clear(); _binding = null; _snapshotDigest = null; _preview = null;
        _skillMinimums.Clear(); _groupMinimums.Clear();
        _availableSkills.Clear(); _availableGroups.Clear();
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
            || !CreationSkillsPhoneAuthority.CanAdoptPreview(state, overview, result, skills, groups)
            || result.Value is not { } preview) return false;
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
        return true;
    }
}
