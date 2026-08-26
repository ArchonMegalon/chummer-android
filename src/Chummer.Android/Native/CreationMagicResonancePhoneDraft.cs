using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

/// <summary>
/// Phone-local chooser state. Only Core-issued typed identities and adept-power levels are held;
/// all budgets, blockers, source facts and legality remain in the latest Core review.
/// </summary>
internal sealed class CreationMagicResonancePhoneDraft
{
    private CharacterCreationMagicResonanceEditorState? _editor;
    private CharacterCreationMagicResonanceSelections _selections = EmptySelections();
    private CharacterCreationMagicResonanceReview? _review;

    public CharacterCreationMagicResonanceSelections Selections => _selections;
    public CharacterCreationMagicResonanceReview? Review => _review;

    public void Bind(
        CharacterCreationMagicResonanceEditorState editor,
        CharacterOverviewState overview)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(overview);
        if (Matches(editor, overview))
            return;
        _editor = null;
        _review = null;
        _selections = EmptySelections();
        if (!CreationMagicResonancePhoneAuthority.IsReady(
                overview.CreationMagicResonance,
                editor,
                overview))
        {
            return;
        }
        _editor = editor;
        _selections = editor.Selections;
    }

    public bool Matches(
        CharacterCreationMagicResonanceEditorState editor,
        CharacterOverviewState overview)
        => _editor is not null
           && CreationMagicResonancePhoneAuthority.IsReady(
               overview.CreationMagicResonance,
               editor,
               overview)
           && CreationMagicResonancePhoneAuthority.EditorEquals(_editor, editor);

    public CharacterCreationMagicResonanceDesktopDraft CreateSingleCandidate(
        CharacterCreationMagicResonanceOptionProjection option)
    {
        CharacterCreationMagicResonanceEditorState editor = RequireEditor();
        if (!CreationMagicResonancePhoneAuthority.IsOptionConfigurable(editor, option))
            throw new InvalidOperationException(
                CharacterCreationMagicResonanceBlockers.OptionDisabled);
        CharacterCreationMagicResonanceSelections candidate = option.Identity.Kind switch
        {
            CharacterCreationMagicResonanceKinds.Tradition => _selections with
            {
                Tradition = _selections.Tradition == option.Identity
                    ? null
                    : option.Identity
            },
            CharacterCreationMagicResonanceKinds.Stream => _selections with
            {
                Stream = _selections.Stream == option.Identity
                    ? null
                    : option.Identity
            },
            _ => throw new InvalidOperationException(
                CharacterCreationMagicResonanceBlockers.OptionInvalid)
        };
        return CreationMagicResonancePhoneAuthority.CreateDraft(editor, candidate);
    }

    public CharacterCreationMagicResonanceDesktopDraft CreateToggleCandidate(
        CharacterCreationMagicResonanceOptionProjection option)
    {
        CharacterCreationMagicResonanceEditorState editor = RequireEditor();
        if (!CreationMagicResonancePhoneAuthority.IsOptionConfigurable(editor, option))
            throw new InvalidOperationException(
                CharacterCreationMagicResonanceBlockers.OptionDisabled);
        IReadOnlyList<CharacterCreationMagicResonanceOptionIdentity> source =
            option.Identity.Kind switch
            {
                CharacterCreationMagicResonanceKinds.Spell => _selections.Spells,
                CharacterCreationMagicResonanceKinds.ComplexForm =>
                    _selections.ComplexForms,
                _ => throw new InvalidOperationException(
                    CharacterCreationMagicResonanceBlockers.OptionInvalid)
            };
        List<CharacterCreationMagicResonanceOptionIdentity> next = source.ToList();
        if (!next.Remove(option.Identity))
            next.Add(option.Identity);
        CharacterCreationMagicResonanceSelections candidate =
            option.Identity.Kind == CharacterCreationMagicResonanceKinds.Spell
                ? _selections with { Spells = next }
                : _selections with { ComplexForms = next };
        return CreationMagicResonancePhoneAuthority.CreateDraft(editor, candidate);
    }

    public CharacterCreationMagicResonanceDesktopDraft CreatePowerLevelCandidate(
        CharacterCreationMagicResonanceOptionProjection option,
        int levels)
    {
        CharacterCreationMagicResonanceEditorState editor = RequireEditor();
        if (!CreationMagicResonancePhoneAuthority.IsOptionConfigurable(editor, option)
            || !string.Equals(
                option.Identity.Kind,
                CharacterCreationMagicResonanceKinds.AdeptPower,
                StringComparison.Ordinal)
            || levels < 0
            || levels > option.MaximumLevels)
        {
            throw new InvalidOperationException(
                CharacterCreationMagicResonanceBlockers.OptionInvalid);
        }
        List<CharacterCreationAdeptPowerAllocation> powers = _selections.AdeptPowers
            .Where(candidate => candidate.Identity != option.Identity)
            .ToList();
        if (levels > 0)
            powers.Add(new(option.Identity, levels));
        return CreationMagicResonancePhoneAuthority.CreateDraft(
            editor,
            _selections with { AdeptPowers = powers });
    }

    public bool TryAdopt(
        CharacterCreationMagicResonanceEditorState editor,
        CharacterOverviewState overview,
        CharacterCreationMagicResonanceReview review)
    {
        if (!Matches(editor, overview)
            || !CreationMagicResonancePhoneAuthority.ReviewMatches(
                editor,
                review,
                requireConfirmable: false))
        {
            return false;
        }
        _selections = review.Draft.Selections;
        _review = review;
        return true;
    }

    public bool IsSelected(CharacterCreationMagicResonanceOptionIdentity identity)
        => identity.Kind switch
        {
            CharacterCreationMagicResonanceKinds.Tradition =>
                _selections.Tradition == identity,
            CharacterCreationMagicResonanceKinds.Stream =>
                _selections.Stream == identity,
            CharacterCreationMagicResonanceKinds.AdeptPower =>
                _selections.AdeptPowers.Any(allocation =>
                    allocation.Identity == identity),
            CharacterCreationMagicResonanceKinds.Spell =>
                _selections.Spells.Contains(identity),
            CharacterCreationMagicResonanceKinds.ComplexForm =>
                _selections.ComplexForms.Contains(identity),
            _ => false
        };

    public int PowerLevels(CharacterCreationMagicResonanceOptionIdentity identity)
        => _selections.AdeptPowers.SingleOrDefault(allocation =>
            allocation.Identity == identity)?.Levels ?? 0;

    private CharacterCreationMagicResonanceEditorState RequireEditor()
        => _editor
           ?? throw new InvalidOperationException(
               CharacterCreationMagicResonanceBlockers.AuthorityUnavailable);

    private static CharacterCreationMagicResonanceSelections EmptySelections()
        => new(null, null, [], [], []);
}
