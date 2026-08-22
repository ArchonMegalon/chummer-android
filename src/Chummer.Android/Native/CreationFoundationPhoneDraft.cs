using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

/// <summary>
/// Phone navigation state for Foundation choices. The state is bound to the complete
/// authoritative Foundation identity and stores only typed option identities. It is not a rules
/// authority and never writes a workspace.
/// </summary>
internal sealed class CreationFoundationPhoneDraft
{
    private CharacterCreationFoundationBinding? _binding;
    private string? _rulesetId;
    private string? _buildMethod;
    private string? _foundationSnapshotDigest;

    public string? ConfirmedMetatypeOptionId { get; private set; }

    public bool Bind(CharacterCreationFoundationInteractionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (Matches(state))
            return false;

        _binding = state.Binding;
        _rulesetId = state.RulesetId;
        _buildMethod = state.BuildMethod;
        _foundationSnapshotDigest = state.FoundationSnapshotDigest;
        ConfirmedMetatypeOptionId = ResolvePendingMetatypeOptionId(state);
        return true;
    }

    public bool Matches(CharacterCreationFoundationInteractionState state)
        => _binding is not null
           && BindingEquals(_binding, state.Binding)
           && string.Equals(_rulesetId, state.RulesetId, StringComparison.Ordinal)
           && string.Equals(_buildMethod, state.BuildMethod, StringComparison.Ordinal)
           && string.Equals(
               _foundationSnapshotDigest,
               state.FoundationSnapshotDigest,
               StringComparison.Ordinal);

    public CharacterCreationLegalOption? ResolveConfirmedMetatype(
        CharacterCreationFoundationInteractionState state)
        => Matches(state)
            ? ResolveUniqueEnabledOption(state, ConfirmedMetatypeOptionId)
            : null;

    public CharacterCreationLegalOption? ResolveCandidate(
        CharacterCreationFoundationInteractionState state,
        string? optionId)
        => Matches(state)
            ? ResolveUniqueOption(state, optionId)
            : null;

    public bool TryConfirmMetatype(
        CharacterCreationFoundationInteractionState state,
        string optionId)
    {
        CharacterCreationLegalOption? option = ResolveCandidate(state, optionId);
        if (option is not { IsEnabled: true }
            || !string.IsNullOrWhiteSpace(option.DisableReasonKey)
            || state.AuthorityBlockers.Count > 0
            || !state.LifeModuleBudget.IsExact
            || state.LifeModuleBudget.Blockers.Count > 0)
        {
            return false;
        }

        ConfirmedMetatypeOptionId = option.OptionId;
        return true;
    }

    private static string? ResolvePendingMetatypeOptionId(
        CharacterCreationFoundationInteractionState state)
    {
        CharacterCreationFoundationDraftLedger? pending = state.PendingDraft;
        if (pending is null
            || pending.CharacterEffectsApplied
            || !pending.WorkspaceId.Equals(state.Binding.WorkspaceId)
            || !string.Equals(pending.SourceDigest, state.Binding.SourceDigest, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(pending.RequestedMetatype))
        {
            return null;
        }

        CharacterCreationLegalOption[] matches = state.MetatypeOptions
            .Where(option =>
                option.IsEnabled
                && string.IsNullOrWhiteSpace(option.DisableReasonKey)
                && !string.IsNullOrWhiteSpace(option.OptionId)
                && string.Equals(
                    option.Label,
                    pending.RequestedMetatype,
                    StringComparison.Ordinal))
            .ToArray();
        return matches.Length == 1 ? matches[0].OptionId : null;
    }

    private static CharacterCreationLegalOption? ResolveUniqueEnabledOption(
        CharacterCreationFoundationInteractionState state,
        string? optionId)
    {
        CharacterCreationLegalOption? option = ResolveUniqueOption(state, optionId);
        return option is { IsEnabled: true }
               && string.IsNullOrWhiteSpace(option.DisableReasonKey)
            ? option
            : null;
    }

    private static CharacterCreationLegalOption? ResolveUniqueOption(
        CharacterCreationFoundationInteractionState state,
        string? optionId)
    {
        if (string.IsNullOrWhiteSpace(optionId))
            return null;
        CharacterCreationLegalOption[] matches = state.MetatypeOptions
            .Where(option =>
                !string.IsNullOrWhiteSpace(option.OptionId)
                && string.Equals(option.OptionId, optionId, StringComparison.Ordinal))
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static bool BindingEquals(
        CharacterCreationFoundationBinding left,
        CharacterCreationFoundationBinding right)
        => left.WorkspaceId.Equals(right.WorkspaceId)
           && left.ContentRevision == right.ContentRevision
           && left.SavedRevision == right.SavedRevision
           && string.Equals(
               left.RawCharacterXmlDigest,
               right.RawCharacterXmlDigest,
               StringComparison.Ordinal)
           && string.Equals(
               left.CharacterDigestSemantics,
               right.CharacterDigestSemantics,
               StringComparison.Ordinal)
           && string.Equals(left.SourceDigest, right.SourceDigest, StringComparison.Ordinal)
           && string.Equals(
               left.SourceDigestSemantics,
               right.SourceDigestSemantics,
               StringComparison.Ordinal)
           && left.SourceFilterApplied == right.SourceFilterApplied
           && left.EnabledSources.SequenceEqual(right.EnabledSources, StringComparer.Ordinal);
}
