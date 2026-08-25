using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

/// <summary>
/// In-memory phone navigation state. Every projection placed in this draft came from Core;
/// only explicit confirmation may persist the exact revision-bound allocation list.
/// </summary>
internal sealed class CreationAttributesPhoneDraft
{
    private readonly Dictionary<string, CharacterCreationAttributeAllocation> _allocations =
        new(StringComparer.Ordinal);
    private CharacterCreationAttributesBinding? _binding;
    private string? _snapshotDigest;
    private CharacterCreationAttributesPreview? _projection;

    public void Bind(
        CharacterCreationAttributesState state,
        CharacterOverviewState overview)
    {
        if (Matches(state, overview))
            return;

        _binding = null;
        _snapshotDigest = null;
        _projection = null;
        _allocations.Clear();
        if (!CreationAttributesPhoneAuthority.IsReady(state, overview))
            return;

        _binding = state.Binding;
        _snapshotDigest = state.SnapshotDigest;
        IEnumerable<CharacterCreationAttributeAllocation> restored =
            state.PendingDraft?.Allocations
            ?? state.Attributes.Select(attribute => new CharacterCreationAttributeAllocation(
                attribute.AttributeId,
                attribute.PriorityPointsSpent,
                attribute.KarmaLevels));
        foreach (CharacterCreationAttributeAllocation allocation in restored)
            _allocations.Add(allocation.AttributeId, allocation);
    }

    public bool Matches(
        CharacterCreationAttributesState state,
        CharacterOverviewState overview)
        => _binding is not null
           && CreationAttributesPhoneAuthority.IsReady(state, overview)
           && CreationAttributesPhoneAuthority.BindingEquals(_binding, state.Binding)
           && CharacterCreationPrerequisiteAuthorityDigest.EqualsFixedTime(
               _snapshotDigest,
               state.SnapshotDigest);

    public IReadOnlyList<CharacterCreationAttributeAllocation> Allocations(
        CharacterCreationAttributesState state)
        => state.Attributes.Select(attribute =>
            _allocations.TryGetValue(attribute.AttributeId, out CharacterCreationAttributeAllocation? allocation)
                ? allocation
                : new CharacterCreationAttributeAllocation(attribute.AttributeId, 0, 0))
            .ToArray();

    public IReadOnlyList<CharacterCreationAttributeProjection> Attributes(
        CharacterCreationAttributesState state)
        => _projection?.Attributes ?? state.Attributes;

    public CharacterCreationBudgetState NormalBudget(CharacterCreationAttributesState state)
        => _projection?.NormalPointBudget ?? state.NormalPointBudget;

    public CharacterCreationBudgetState SpecialBudget(CharacterCreationAttributesState state)
        => _projection?.SpecialPointBudget ?? state.SpecialPointBudget;

    public CharacterCreationBudgetState KarmaBudget(CharacterCreationAttributesState state)
        => _projection?.CreationKarmaBudget ?? state.CreationKarmaBudget;

    public CharacterCreationAttributeProjection? Attribute(
        CharacterCreationAttributesState state,
        string attributeId)
        => Attributes(state).SingleOrDefault(attribute => string.Equals(
            attribute.AttributeId,
            attributeId,
            StringComparison.Ordinal));

    public IReadOnlyList<CharacterCreationAttributeAllocation>? ChangedAllocations(
        CharacterCreationAttributesState state,
        string attributeId,
        int priorityDelta,
        int karmaDelta)
    {
        CharacterCreationAttributeAllocation? current = Allocations(state)
            .SingleOrDefault(allocation => string.Equals(
                allocation.AttributeId,
                attributeId,
                StringComparison.Ordinal));
        if (current is null)
            return null;

        int priority;
        int karma;
        try
        {
            priority = checked(current.PriorityPoints + priorityDelta);
            karma = checked(current.KarmaLevels + karmaDelta);
        }
        catch (OverflowException)
        {
            return null;
        }
        if (priority < 0 || karma < 0)
            return null;

        return Allocations(state)
            .Select(allocation => string.Equals(
                    allocation.AttributeId,
                    attributeId,
                    StringComparison.Ordinal)
                ? allocation with { PriorityPoints = priority, KarmaLevels = karma }
                : allocation)
            .ToArray();
    }

    public bool TryAdopt(
        CharacterCreationAttributesState state,
        CharacterOverviewState overview,
        CharacterCreationFoundationResult<CharacterCreationAttributesPreview> result,
        IReadOnlyList<CharacterCreationAttributeAllocation> allocations)
    {
        if (!Matches(state, overview)
            || !CreationAttributesPhoneAuthority.CanAdoptPreview(
                state,
                overview,
                result,
                allocations)
            || result.Value is not { } preview)
        {
            return false;
        }

        _allocations.Clear();
        foreach (CharacterCreationAttributeAllocation allocation in allocations)
            _allocations.Add(allocation.AttributeId, allocation);
        _projection = preview;
        return true;
    }
}
