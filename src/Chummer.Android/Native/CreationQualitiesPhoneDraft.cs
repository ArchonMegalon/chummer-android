using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

/// <summary>Ephemeral chooser state; only stable Core OptionIds cross the preview boundary.</summary>
internal sealed class CreationQualitiesPhoneDraft
{
    private CharacterCreationQualitiesBinding? _binding;
    private string? _snapshotDigest;
    private readonly HashSet<string> _selectedOptionIds = new(StringComparer.Ordinal);
    private CharacterCreationQualitiesPreview? _preview;

    public IReadOnlyList<string> SelectedOptionIds => _selectedOptionIds
        .OrderBy(static item => item, StringComparer.Ordinal)
        .ToArray();

    public CharacterCreationQualitiesPreview? Preview => _preview;

    public void Bind(CharacterCreationQualitiesState state, CharacterOverviewState overview)
    {
        if (Matches(state, overview))
            return;
        _binding = null;
        _snapshotDigest = null;
        _preview = null;
        _selectedOptionIds.Clear();
        if (!CreationQualitiesPhoneAuthority.IsReady(state, overview))
            return;
        _binding = state.Binding;
        _snapshotDigest = state.SnapshotDigest;
        foreach (string optionId in state.PendingDraft?.SelectedOptionIds ?? [])
            _selectedOptionIds.Add(optionId);
        _preview = state.Preview;
    }

    public bool Matches(CharacterCreationQualitiesState state, CharacterOverviewState overview)
        => _binding is not null
           && CreationQualitiesPhoneAuthority.IsReady(state, overview)
           && CreationQualitiesPhoneAuthority.BindingEquals(_binding, state.Binding)
           && CharacterCreationQualitiesRules.DigestsEqual(_snapshotDigest, state.SnapshotDigest);

    public bool IsSelected(string optionId) => _selectedOptionIds.Contains(optionId);

    public IReadOnlyList<string> WithToggle(CharacterCreationQualitiesDesktopOption option)
    {
        ArgumentNullException.ThrowIfNull(option);
        HashSet<string> next = new(_selectedOptionIds, StringComparer.Ordinal);
        if (!CreationQualitiesPhoneAuthority.IsOptionConfigurable(option))
            return next.OrderBy(static item => item, StringComparer.Ordinal).ToArray();
        if (!next.Remove(option.OptionId))
            next.Add(option.OptionId);
        return next.OrderBy(static item => item, StringComparer.Ordinal).ToArray();
    }

    public bool TryAdopt(
        CharacterCreationQualitiesState state,
        CharacterOverviewState overview,
        CharacterCreationFoundationResult<CharacterCreationQualitiesPreview> result,
        IReadOnlyList<string> selectedOptionIds)
    {
        if (!Matches(state, overview)
            || !CreationQualitiesPhoneAuthority.CanDisplayPreview(
                state,
                overview,
                result,
                selectedOptionIds)
            || result.Value is not { } preview)
        {
            return false;
        }
        _selectedOptionIds.Clear();
        foreach (string optionId in selectedOptionIds)
            _selectedOptionIds.Add(optionId);
        _preview = preview;
        return true;
    }
}
