using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

internal sealed class CreationSkillsPhoneDraft
{
    private readonly Dictionary<(string Kind, string Id), CharacterCreationSkillAllocation> _skills = [];
    private readonly Dictionary<string, CharacterCreationSkillGroupAllocation> _groups = new(StringComparer.Ordinal);
    private CharacterCreationSkillsBinding? _binding;
    private string? _snapshotDigest;
    private CharacterCreationSkillsPreview? _preview;

    public void Bind(CharacterCreationSkillsState state, CharacterOverviewState overview)
    {
        if (Matches(state, overview)) return;
        _skills.Clear(); _groups.Clear(); _binding = null; _snapshotDigest = null; _preview = null;
        if (!CreationSkillsPhoneAuthority.IsReady(state, overview)) return;
        _binding = state.Binding; _snapshotDigest = state.SnapshotDigest;
        foreach (CharacterCreationSkillAllocation item in state.PendingDraft?.Allocations ?? [])
            _skills[(item.Kind, item.SourceSkillId)] = item;
        foreach (CharacterCreationSkillGroupAllocation item in state.PendingDraft?.GroupAllocations ?? [])
            _groups[item.GroupId] = item;
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

    public IReadOnlyList<CharacterCreationSkillAllocation> WithSkill(
        CharacterCreationSkillCatalogEntry source, int delta, bool native = false)
    {
        _skills.TryGetValue((source.Kind, source.SourceSkillId), out CharacterCreationSkillAllocation? current);
        if (native)
        {
            if (!source.CanBeNativeLanguage)
                return Skills;
            return Skills.Where(item => item.Kind != source.Kind || item.SourceSkillId != source.SourceSkillId)
                .Append(new(source.SourceSkillId, source.Kind, null, null, true))
                .ToArray();
        }

        int next = Math.Max(0, (current is { IsNativeLanguage: false }
            ? current.Rating.GetValueOrDefault()
            : 0) + delta);
        return Skills.Where(item => item.Kind != source.Kind || item.SourceSkillId != source.SourceSkillId)
            .Append(new(source.SourceSkillId, source.Kind, next,
                next > 0 && current is { IsNativeLanguage: false } ? current.SpecializationOptionId : null,
                false))
            .Where(item => item.Rating > 0)
            .ToArray();
    }

    public IReadOnlyList<CharacterCreationSkillGroupAllocation> WithGroup(
        CharacterCreationSkillGroupCatalogEntry source, int delta)
    {
        _groups.TryGetValue(source.GroupId, out CharacterCreationSkillGroupAllocation? current);
        int next = Math.Max(0, (current?.Rating ?? 0) + delta);
        return Groups.Where(item => item.GroupId != source.GroupId)
            .Append(new(source.GroupId, next)).Where(item => item.Rating > 0).ToArray();
    }

    public IReadOnlyList<CharacterCreationSkillAllocation> WithSpecialization(
        CharacterCreationSkillCatalogEntry source,
        string optionId)
    {
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
