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

    // A worker prepares its own draft; a departed page or child editor must
    // never share a mutable selection set with an in-flight display read.
    public CreationQualitiesPhoneDraft Copy()
    {
        var copy = new CreationQualitiesPhoneDraft
        {
            _binding = _binding,
            _snapshotDigest = _snapshotDigest,
            _preview = _preview
        };
        copy._selectedOptionIds.UnionWith(_selectedOptionIds);
        return copy;
    }

    // Return the result of this call's full validation, not a retained admission.
    // A caller preparing the same display need not immediately hash it again
    // just to learn whether binding succeeded. Preview/apply keep their own guards.
    public bool Bind(CharacterCreationQualitiesState state, CharacterOverviewState overview)
    {
        bool ready = CreationQualitiesPhoneAuthority.IsReady(state, overview);
        if (ready && _binding is not null
            && CreationQualitiesPhoneAuthority.BindingEquals(_binding, state.Binding)
            && CharacterCreationQualitiesRules.DigestsEqual(_snapshotDigest, state.SnapshotDigest))
            return true;
        _binding = null;
        _snapshotDigest = null;
        _preview = null;
        _selectedOptionIds.Clear();
        if (!ready)
            return false;
        _binding = state.Binding;
        _snapshotDigest = state.SnapshotDigest;
        foreach (string optionId in state.PendingDraft?.SelectedOptionIds ?? [])
            _selectedOptionIds.Add(optionId);
        _preview = state.Preview;
        return true;
    }

    public bool Matches(CharacterCreationQualitiesState state, CharacterOverviewState overview)
        => _binding is not null
           && CreationQualitiesPhoneAuthority.IsReady(state, overview)
           && CreationQualitiesPhoneAuthority.BindingEquals(_binding, state.Binding)
           && CharacterCreationQualitiesRules.DigestsEqual(_snapshotDigest, state.SnapshotDigest);

    public bool IsSelected(string optionId) => _selectedOptionIds.Contains(optionId);

    // Filter against Core's complete proposed selection, not a second phone
    // implementation of Karma, duplicate, granted-quality or profile limits.
    // Run once during background appearance preparation; search/paging reuse it.
    public IReadOnlyList<CharacterCreationQualitiesDesktopOption> AvailableOptions(
        CharacterCreationQualitiesState state,
        CharacterOverviewState overview,
        CharacterCreationQualitiesEditorState editor,
        CancellationToken cancellationToken)
    {
        if (!Matches(state, overview)) return [];
        var available = new List<CharacterCreationQualitiesDesktopOption>();
        var additions = editor.Options
            .Where(option => !IsSelected(option.OptionId)
                && CreationQualitiesPhoneAuthority.IsOptionConfigurable(option))
            .Select(option => option.OptionId)
            .ToArray();
        // Core snapshots and validates the shared catalog once for this load.
        // No admission is cached across appearances, owner changes or revisions.
        var previews = CharacterCreationQualitiesRules.EvaluateAdditions(
            new(state.Binding, state.Authority, SelectedOptionIds), additions, cancellationToken);
        int additionIndex = 0;
        foreach (var option in editor.Options)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Keep chosen entries reachable for removal, even in a draft whose
            // budget still needs repair. Never silently discard the selection.
            if (IsSelected(option.OptionId))
            {
                available.Add(option);
                continue;
            }
            if (!CreationQualitiesPhoneAuthority.IsOptionConfigurable(option)) continue;
            var preview = previews[additionIndex++];
            // Metagenic pairs are assembled one choice at a time. Core still
            // requires balance before review/confirmation can be enabled.
            if (preview.Blockers.All(blocker => blocker == CharacterCreationQualitiesBlockers.MetagenicImbalanced))
                available.Add(option);
        }
        return available;
    }

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
