using Chummer.Contracts.Characters;
using Chummer.Application.Characters;
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
    private CharacterCreationAttributesState? _preparedState;
    private CharacterCreationAttributesService.DraftSession? _session;
    private string[]? _playerChosenAttributeIds;

    // Null means a legacy draft whose individual choices are unknown. Returning
    // detached values keeps speculative affordances and workers out of UI state.
    public IReadOnlyList<string>? PlayerChosenAttributeIds => _playerChosenAttributeIds?.ToArray();

    public IReadOnlyList<string>? PlayerChosenIdsForChoice(string attributeId)
        => _playerChosenAttributeIds is null ? null
            : _playerChosenAttributeIds.Append(attributeId).Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal).ToArray();

    public bool HasPreparedState(CharacterCreationAttributesState state)
        => ReferenceEquals(_preparedState, state) && _session is not null;

    public object? ProjectionIdentity => _projection ?? (object?)_preparedState;

    public void RetireSession() { _session = null; _preparedState = null; }

    public CharacterCreationFoundationResult<CharacterCreationAttributesPreview> PreviewPrepared(
        CharacterCreationAttributesState state, IReadOnlyList<CharacterCreationAttributeAllocation> allocations)
        => PreviewPrepared(state, allocations, PlayerChosenAttributeIds);

    public CharacterCreationFoundationResult<CharacterCreationAttributesPreview> PreviewPrepared(
        CharacterCreationAttributesState state, IReadOnlyList<CharacterCreationAttributeAllocation> allocations,
        IReadOnlyList<string>? playerChosenAttributeIds)
        => HasPreparedState(state) ? _session!.Preview(allocations, playerChosenAttributeIds)
            : new(CharacterCreationFoundationOutcomes.Blocked, null,
                [CharacterCreationAttributesBlockers.StaleWorkspaceRevision]);

    public Func<bool> PrepareSelection(CharacterCreationAttributesState state, CharacterOverviewState overview,
        IReadOnlyList<CharacterCreationAttributeAllocation> allocations, IReadOnlyList<string>? playerChosenAttributeIds)
    {
        var session = HasPreparedState(state) ? _session : null;
        var copied = allocations.ToArray();
        var copiedIds = playerChosenAttributeIds?.ToArray();
        return () => session is not null && CreationAttributesPhoneAuthority.CanAdoptPreview(
            state, overview, session.Preview(copied, copiedIds), copied, copiedIds);
    }

    // Worker preparation owns a copy; it must never enumerate or update the UI draft.
    internal CreationAttributesPhoneDraft Copy()
    {
        var copy = new CreationAttributesPhoneDraft
        {
            _binding = _binding,
            _snapshotDigest = _snapshotDigest,
            _projection = _projection,
            _playerChosenAttributeIds = _playerChosenAttributeIds?.ToArray()
        };
        foreach (var allocation in _allocations)
            copy._allocations.Add(allocation.Key, allocation.Value);
        return copy;
    }

    public void Bind(
        CharacterCreationAttributesState state,
        CharacterOverviewState overview)
    {
        if (Matches(state, overview))
        {
            if (!HasPreparedState(state))
            {
                _session = CharacterCreationAttributesService.CreateDraftSession(state);
                _preparedState = state;
            }
            return;
        }

        _binding = null;
        _snapshotDigest = null;
        _projection = null;
        _playerChosenAttributeIds = null;
        _allocations.Clear();
        RetireSession();
        if (!CreationAttributesPhoneAuthority.IsReady(state, overview))
            return;

        _binding = state.Binding;
        _snapshotDigest = state.SnapshotDigest;
        _playerChosenAttributeIds = state.PendingDraft is { } pending
            ? pending.PlayerChosenAttributeIds?.ToArray() : [];
        IEnumerable<CharacterCreationAttributeAllocation> restored =
            state.PendingDraft?.Allocations
            ?? state.Attributes.Select(attribute => new CharacterCreationAttributeAllocation(
                attribute.AttributeId,
                attribute.PriorityPointsSpent,
                attribute.KarmaLevels));
        foreach (CharacterCreationAttributeAllocation allocation in restored)
            _allocations.Add(allocation.AttributeId, allocation);
        _session = CharacterCreationAttributesService.CreateDraftSession(state);
        _preparedState = state;
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
        => TryAdopt(state, overview, result, allocations, result.Value?.PlayerChosenAttributeIds);

    public bool TryAdopt(
        CharacterCreationAttributesState state,
        CharacterOverviewState overview,
        CharacterCreationFoundationResult<CharacterCreationAttributesPreview> result,
        IReadOnlyList<CharacterCreationAttributeAllocation> allocations,
        IReadOnlyList<string>? playerChosenAttributeIds)
    {
        if (!Matches(state, overview)
            || !CreationAttributesPhoneAuthority.CanAdoptPreview(
                state,
                overview,
                result,
                allocations,
                playerChosenAttributeIds)
            || _playerChosenAttributeIds is { Length: > 0 } retainedIds
                && (playerChosenAttributeIds is null || retainedIds.Any(id => !playerChosenAttributeIds.Contains(id, StringComparer.Ordinal)))
            || result.Value is not { } preview)
        {
            return false;
        }

        _allocations.Clear();
        foreach (CharacterCreationAttributeAllocation allocation in allocations)
            _allocations.Add(allocation.AttributeId, allocation);
        _projection = preview;
        _playerChosenAttributeIds = playerChosenAttributeIds?.ToArray();
        return true;
    }
}
