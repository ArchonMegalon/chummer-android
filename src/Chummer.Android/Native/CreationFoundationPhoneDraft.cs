using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;
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
    public string? ConfirmedNationalityModuleId { get; private set; }
    public string? ConfirmedNationalityVersionId { get; private set; }

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
        ResolvePendingNationalitySelection(state);
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

    public LifeModuleLegalOptionDto? ResolveConfirmedNationality(
        CharacterCreationFoundationInteractionState state)
        => Matches(state)
            ? CreationFoundationPhoneAuthority.ResolveUniqueModule(
                state,
                ConfirmedNationalityModuleId)
            : null;

    public LifeModuleVersionProjectionDto? ResolveConfirmedNationalityVersion(
        CharacterCreationFoundationInteractionState state)
    {
        LifeModuleLegalOptionDto? module = ResolveConfirmedNationality(state);
        return module is null
            ? null
            : CreationFoundationPhoneAuthority.ResolveUniqueVersion(
                module,
                ConfirmedNationalityVersionId);
    }

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

        if (!string.Equals(ConfirmedMetatypeOptionId, option.OptionId, StringComparison.Ordinal))
        {
            ConfirmedNationalityModuleId = null;
            ConfirmedNationalityVersionId = null;
        }
        ConfirmedMetatypeOptionId = option.OptionId;
        return true;
    }

    public bool TryConfirmNationality(
        CharacterCreationFoundationInteractionState state,
        string moduleId,
        string? versionId)
    {
        CharacterCreationLegalOption? metatype = ResolveConfirmedMetatype(state);
        LifeModuleLegalOptionDto? module = CreationFoundationPhoneAuthority.ResolveUniqueModule(
            state,
            moduleId);
        LifeModuleVersionProjectionDto? version = module is null
            ? null
            : CreationFoundationPhoneAuthority.ResolveUniqueVersion(module, versionId);
        if (module is null
            || module.Versions.Count == 0 && !string.IsNullOrWhiteSpace(versionId)
            || module.Versions.Count > 0 && version is null
            || !CreationFoundationPhoneAuthority.CanReviewSelection(
                state,
                module,
                version,
                metatype))
        {
            return false;
        }

        ConfirmedNationalityModuleId = module.ModuleId;
        ConfirmedNationalityVersionId = version?.VersionId;
        return true;
    }

    public IReadOnlyDictionary<string, string> ResolvePendingFollowUpValues(
        CharacterCreationFoundationInteractionState state)
    {
        CharacterCreationFoundationDraftLedger? pending = MatchingPendingDraft(state);
        LifeModuleLegalOptionDto? module = ResolveConfirmedNationality(state);
        LifeModuleVersionProjectionDto? version = ResolveConfirmedNationalityVersion(state);
        if (pending is null
            || module is null
            || !string.Equals(
                pending.Selection.ModuleId,
                module.ModuleId,
                StringComparison.Ordinal)
            || !string.Equals(
                pending.Selection.VersionId,
                version?.VersionId,
                StringComparison.Ordinal))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        LifeModuleFollowUpPromptDto[] prompts = module.FollowUps
            .Concat(version?.FollowUps ?? [])
            .ToArray();
        if (prompts.GroupBy(prompt => prompt.PromptId, StringComparer.Ordinal)
            .Any(group => group.Count() != 1))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var restored = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string promptId, string value) in pending.FollowUpValues)
        {
            LifeModuleFollowUpPromptDto? prompt = prompts.SingleOrDefault(candidate =>
                string.Equals(candidate.PromptId, promptId, StringComparison.Ordinal));
            if (prompt is null
                || prompt.Options.Count(option =>
                    option.IsEnabled
                    && string.Equals(option.SourceValue, value, StringComparison.Ordinal)) != 1)
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }
            restored[promptId] = value;
        }

        if (prompts.Any(prompt => prompt.IsRequired && !restored.ContainsKey(prompt.PromptId)))
            return new Dictionary<string, string>(StringComparer.Ordinal);
        return restored;
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

    private void ResolvePendingNationalitySelection(
        CharacterCreationFoundationInteractionState state)
    {
        ConfirmedNationalityModuleId = null;
        ConfirmedNationalityVersionId = null;
        CharacterCreationFoundationDraftLedger? pending = MatchingPendingDraft(state);
        CharacterCreationLegalOption? metatype = ResolveConfirmedMetatype(state);
        LifeModuleLegalOptionDto? module = pending is null
            ? null
            : CreationFoundationPhoneAuthority.ResolveUniqueModule(
                state,
                pending.Selection.ModuleId);
        LifeModuleVersionProjectionDto? version = module is null
            ? null
            : CreationFoundationPhoneAuthority.ResolveUniqueVersion(
                module,
                pending?.Selection.VersionId);
        if (pending is null
            || module is null
            || module.Versions.Count == 0 && !string.IsNullOrWhiteSpace(pending.Selection.VersionId)
            || module.Versions.Count > 0 && version is null
            || !CreationFoundationPhoneAuthority.CanReviewSelection(
                state,
                module,
                version,
                metatype))
        {
            return;
        }

        ConfirmedNationalityModuleId = module.ModuleId;
        ConfirmedNationalityVersionId = version?.VersionId;
    }

    private static CharacterCreationFoundationDraftLedger? MatchingPendingDraft(
        CharacterCreationFoundationInteractionState state)
    {
        CharacterCreationFoundationDraftLedger? pending = state.PendingDraft;
        return pending is not null
               && !pending.CharacterEffectsApplied
               && pending.WorkspaceId.Equals(state.Binding.WorkspaceId)
               && string.Equals(
                   pending.SourceDigest,
                   state.Binding.SourceDigest,
                   StringComparison.Ordinal)
               && pending.RequirementEvaluations.All(requirement =>
                   !requirement.RequiresCharacterAuthority || requirement.IsMet)
            ? pending
            : null;
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
