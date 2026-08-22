using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

/// <summary>
/// Fail-closed phone projection over Foundation-owned Nationality candidates. It never changes
/// the catalog projection: candidates which need metatype evaluation remain disabled in the
/// source DTO and can only be carried into Core Preview when their typed requirement shape is
/// exact and matches the explicitly selected authoritative metatype.
/// </summary>
internal static class CreationFoundationPhoneAuthority
{
    public static LifeModuleLegalOptionDto? ResolveUniqueModule(
        CharacterCreationFoundationInteractionState state,
        string? moduleId)
    {
        if (string.IsNullOrWhiteSpace(moduleId))
            return null;
        LifeModuleLegalOptionDto[] matches = state.NationalityOptions
            .Where(candidate => string.Equals(
                candidate.ModuleId,
                moduleId,
                StringComparison.Ordinal))
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    public static LifeModuleVersionProjectionDto? ResolveUniqueVersion(
        LifeModuleLegalOptionDto module,
        string? versionId)
    {
        if (string.IsNullOrWhiteSpace(versionId))
            return null;
        LifeModuleVersionProjectionDto[] matches = module.Versions
            .Where(candidate => string.Equals(
                candidate.VersionId,
                versionId,
                StringComparison.Ordinal))
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    public static bool CanOpenModule(
        CharacterCreationFoundationInteractionState state,
        LifeModuleLegalOptionDto module,
        CharacterCreationLegalOption? selectedMetatype)
        => module.Versions.Count == 0
            ? CanReviewSelection(state, module, null, selectedMetatype)
            : module.Versions.Any(version =>
                CanReviewSelection(state, module, version, selectedMetatype));

    public static bool CanReviewSelection(
        CharacterCreationFoundationInteractionState state,
        LifeModuleLegalOptionDto module,
        LifeModuleVersionProjectionDto? version,
        CharacterCreationLegalOption? selectedMetatype)
    {
        if (selectedMetatype is not { IsEnabled: true }
            || string.IsNullOrWhiteSpace(selectedMetatype.OptionId)
            || string.IsNullOrWhiteSpace(selectedMetatype.Label)
            || !string.IsNullOrWhiteSpace(selectedMetatype.DisableReasonKey)
            || state.MetatypeOptions.Count(candidate =>
                candidate.IsEnabled
                && string.IsNullOrWhiteSpace(candidate.DisableReasonKey)
                && string.Equals(
                    candidate.OptionId,
                    selectedMetatype.OptionId,
                    StringComparison.Ordinal)
                && string.Equals(
                    candidate.Label,
                    selectedMetatype.Label,
                    StringComparison.Ordinal)) != 1
            || state.AuthorityBlockers.Count > 0
            || !state.LifeModuleBudget.IsExact
            || state.LifeModuleBudget.Blockers.Count > 0
            || !HasExactCandidateIdentity(state.NationalityOptions, module, version)
            || module.StageOrder != LifeModuleJourneyStageOrders.Nationality
            || !string.Equals(
                module.StageId,
                CharacterCreationLifeModuleStageIds.Nationality,
                StringComparison.OrdinalIgnoreCase)
            || module.CanRepeat
            || !HasExactCandidateIdentityCostAndSource(
                module.ModuleId,
                module.Name,
                module.KarmaCost,
                module.KarmaRaw,
                module.KarmaIsExact,
                module.Source,
                module.SourceAnchorIds)
            || version is not null
            && !HasExactCandidateIdentityCostAndSource(
                version.VersionId,
                version.Label,
                version.KarmaCost,
                version.KarmaRaw,
                version.KarmaIsExact,
                version.Source,
                version.SourceAnchorIds))
        {
            return false;
        }

        IReadOnlyList<LifeModuleRequirementProjectionDto> requirements = module.Requirements
            .Concat(version?.Requirements ?? [])
            .ToArray();
        bool projectedEnabled = module.IsEnabled && (version is null || version.IsEnabled);
        if (projectedEnabled)
        {
            return module.AuthorityBlockers.Count == 0
                   && (version?.AuthorityBlockers.Count ?? 0) == 0
                   && requirements.All(requirement =>
                       requirement.IsMet
                       && string.IsNullOrWhiteSpace(requirement.DisableReasonKey));
        }

        return IsMetatypeEvaluationCandidate(
            state,
            module,
            version,
            selectedMetatype.Label);
    }

    public static bool IsMetatypeEvaluationCandidate(
        CharacterCreationFoundationInteractionState state,
        LifeModuleLegalOptionDto module,
        CharacterCreationLegalOption? selectedMetatype)
    {
        if (module.IsEnabled || selectedMetatype is null)
            return false;
        return module.Versions.Count == 0
            ? IsMetatypeEvaluationCandidate(
                state,
                module,
                null,
                selectedMetatype.Label)
            : module.Versions.Any(version =>
                IsMetatypeEvaluationCandidate(
                    state,
                    module,
                    version,
                    selectedMetatype.Label));
    }

    public static bool IsMetatypeEvaluationCandidate(
        CharacterCreationFoundationInteractionState state,
        LifeModuleLegalOptionDto module,
        LifeModuleVersionProjectionDto version,
        CharacterCreationLegalOption? selectedMetatype)
        => !version.IsEnabled
           && selectedMetatype is not null
           && IsMetatypeEvaluationCandidate(
               state,
               module,
               version,
               selectedMetatype.Label);

    private static bool IsMetatypeEvaluationCandidate(
        CharacterCreationFoundationInteractionState state,
        LifeModuleLegalOptionDto module,
        LifeModuleVersionProjectionDto? version,
        string selectedMetatypeLabel)
    {
        if (!HasExactCandidateIdentity(state.NationalityOptions, module, version)
            || module.StageOrder != LifeModuleJourneyStageOrders.Nationality
            || !string.Equals(
                module.StageId,
                CharacterCreationLifeModuleStageIds.Nationality,
                StringComparison.OrdinalIgnoreCase)
            || module.CanRepeat
            || !HasExactCandidateIdentityCostAndSource(
                module.ModuleId,
                module.Name,
                module.KarmaCost,
                module.KarmaRaw,
                module.KarmaIsExact,
                module.Source,
                module.SourceAnchorIds)
            || version is not null
            && !HasExactCandidateIdentityCostAndSource(
                version.VersionId,
                version.Label,
                version.KarmaCost,
                version.KarmaRaw,
                version.KarmaIsExact,
                version.Source,
                version.SourceAnchorIds))
        {
            return false;
        }

        LifeModuleRequirementProjectionDto[] requirements = module.Requirements
            .Concat(version?.Requirements ?? [])
            .ToArray();
        LifeModuleRequirementProjectionDto[] unresolved = requirements
            .Where(static requirement =>
                !requirement.IsMet || requirement.RequiresCharacterAuthority)
            .ToArray();
        if (unresolved.Length == 0
            || requirements.Any(requirement =>
                !string.IsNullOrWhiteSpace(requirement.DisableReasonKey)
                && !string.Equals(
                    requirement.DisableReasonKey,
                    CharacterCreationFoundationBlockers.CharacterEligibilityAuthorityRequired,
                    StringComparison.Ordinal))
            || !HasOnlyTypedMetatypeRequirements(unresolved))
        {
            return false;
        }

        string[] blockers = module.AuthorityBlockers
            .Concat(version?.AuthorityBlockers ?? [])
            .Concat(unresolved.Select(static requirement =>
                requirement.DisableReasonKey ?? string.Empty))
            .Where(static blocker => !string.IsNullOrWhiteSpace(blocker))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return HasOnlyEligibilityAuthorityBlocker(blockers)
               && unresolved.All(requirement =>
                   requirement.AcceptedValues.Contains(
                       selectedMetatypeLabel,
                       StringComparer.OrdinalIgnoreCase));
    }

    private static bool HasExactCandidateIdentity(
        IReadOnlyList<LifeModuleLegalOptionDto> modules,
        LifeModuleLegalOptionDto module,
        LifeModuleVersionProjectionDto? version)
    {
        if (string.IsNullOrWhiteSpace(module.ModuleId)
            || modules.Count(candidate => string.Equals(
                candidate.ModuleId,
                module.ModuleId,
                StringComparison.Ordinal)) != 1)
        {
            return false;
        }

        if (module.Versions.Count == 0)
            return version is null;
        return version is not null
               && !string.IsNullOrWhiteSpace(version.VersionId)
               && module.Versions.Count(candidate => string.Equals(
                   candidate.VersionId,
                   version.VersionId,
                   StringComparison.Ordinal)) == 1;
    }

    private static bool HasExactCandidateIdentityCostAndSource(
        string id,
        string label,
        decimal karmaCost,
        string karmaRaw,
        bool karmaIsExact,
        string source,
        IReadOnlyList<string> sourceAnchorIds)
        => !string.IsNullOrWhiteSpace(id)
           && string.Equals(id, id.Trim(), StringComparison.Ordinal)
           && !string.IsNullOrWhiteSpace(label)
           && string.Equals(label, label.Trim(), StringComparison.Ordinal)
           && karmaIsExact
           && karmaCost >= 0
           && !string.IsNullOrWhiteSpace(karmaRaw)
           && string.Equals(karmaRaw, karmaRaw.Trim(), StringComparison.Ordinal)
           && !string.IsNullOrWhiteSpace(source)
           && string.Equals(source, source.Trim(), StringComparison.Ordinal)
           && sourceAnchorIds.Count > 0
           && sourceAnchorIds.All(static anchor => !string.IsNullOrWhiteSpace(anchor));

    private static bool HasOnlyEligibilityAuthorityBlocker(IReadOnlyList<string> blockers)
        => blockers.Count == 1
           && string.Equals(
               blockers[0],
               CharacterCreationFoundationBlockers.CharacterEligibilityAuthorityRequired,
               StringComparison.Ordinal);

    private static bool HasOnlyTypedMetatypeRequirements(
        IReadOnlyList<LifeModuleRequirementProjectionDto> requirements)
        => requirements.Count > 0
           && requirements.All(requirement =>
               requirement.RequiresCharacterAuthority
               && !requirement.IsMet
               && !string.IsNullOrWhiteSpace(requirement.RequirementId)
               && string.Equals(requirement.Operator, "oneof", StringComparison.OrdinalIgnoreCase)
               && string.Equals(requirement.SubjectKind, "metatype", StringComparison.OrdinalIgnoreCase)
               && requirement.AcceptedValues.Count > 0
               && requirement.AcceptedValues.All(static value =>
                   !string.IsNullOrWhiteSpace(value)));
}
