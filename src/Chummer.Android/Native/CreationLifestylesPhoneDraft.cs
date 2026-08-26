using System.Security.Cryptography;
using System.Text;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

/// <summary>
/// Phone-local typed Lifestyle draft. It retains stable catalog identities only;
/// Core owns legality, economics, the preservation proof, and persistence.
/// </summary>
internal sealed class CreationLifestylesPhoneDraft
{
    private CharacterCreationLifestyleBinding? _binding;
    private string? _snapshotDigest;
    private string? _originalDigest;
    private CharacterCreationLifestyleConfiguration? _original;
    private CharacterCreationLifestyleConfiguration? _current;
    private string _mutationKind = string.Empty;

    public bool BindCreate(
        CharacterCreationLifestylesInteractionState state,
        Guid lifestyleId,
        string optionId)
    {
        if (Matches(state, lifestyleId, CharacterCreationLifestyleMutationKinds.Create))
            return false;
        CharacterCreationLifestyleCatalogOption? option =
            CreationLifestylesPhoneAuthority.ResolveUniqueSelectableOption(state, optionId);
        if (lifestyleId == Guid.Empty || option is null)
            return false;

        _binding = state.Binding;
        _snapshotDigest = state.SnapshotDigest;
        _originalDigest = null;
        _mutationKind = CharacterCreationLifestyleMutationKinds.Create;
        _original = null;
        _current = new CharacterCreationLifestyleConfiguration(
            lifestyleId,
            option.OptionId,
            option.Name,
            CharacterCreationLifestyleStyleIds.Standard,
            option.DefaultIncrementId,
            Increments: 1,
            Percentage: 100m,
            Roommates: 0,
            SplitCostWithRoommates: false,
            TrustFund: false,
            Area: 0,
            Comforts: 0,
            Security: 0,
            BonusLifestylePoints: 0,
            City: string.Empty,
            District: string.Empty,
            Borough: string.Empty,
            Qualities: []);
        return true;
    }

    public bool BindEdit(
        CharacterCreationLifestylesInteractionState state,
        CharacterCreationLifestyleProjection projection)
    {
        if (Matches(state, projection.Configuration.LifestyleId, CharacterCreationLifestyleMutationKinds.Edit)
            && string.Equals(_originalDigest, projection.LifestyleDigest, StringComparison.Ordinal))
        {
            return false;
        }
        if (!CreationLifestylesPhoneAuthority.IsExactProjection(projection))
            return false;
        _binding = state.Binding;
        _snapshotDigest = state.SnapshotDigest;
        _originalDigest = projection.LifestyleDigest;
        _mutationKind = CharacterCreationLifestyleMutationKinds.Edit;
        _original = projection.Configuration;
        _current = projection.Configuration;
        return true;
    }

    public bool Matches(
        CharacterCreationLifestylesInteractionState state,
        Guid lifestyleId,
        string mutationKind)
        => _binding is not null
           && _current is { } current
           && current.LifestyleId == lifestyleId
           && string.Equals(_mutationKind, mutationKind, StringComparison.Ordinal)
           && CreationLifestylesPhoneAuthority.BindingEquals(_binding, state.Binding)
           && string.Equals(_snapshotDigest, state.SnapshotDigest, StringComparison.Ordinal);

    public CharacterCreationLifestyleConfiguration? Current(
        CharacterCreationLifestylesInteractionState state)
        => _current is { } current
           && Matches(state, current.LifestyleId, _mutationKind)
            ? current
            : null;

    public bool TrySelectBase(
        CharacterCreationLifestylesInteractionState state,
        string optionId)
    {
        if (!TryCurrent(state, out CharacterCreationLifestyleConfiguration current)
            || CreationLifestylesPhoneAuthority.ResolveUniqueSelectableOption(state, optionId) is not { } option)
        {
            return false;
        }
        _current = current with
        {
            BaseLifestyleOptionId = option.OptionId,
            IncrementId = option.DefaultIncrementId,
            Area = string.Equals(current.StyleId, CharacterCreationLifestyleStyleIds.Standard, StringComparison.Ordinal)
                ? 0 : Math.Min(current.Area, option.MaximumArea),
            Comforts = string.Equals(current.StyleId, CharacterCreationLifestyleStyleIds.Standard, StringComparison.Ordinal)
                ? 0 : Math.Min(current.Comforts, option.MaximumComforts),
            Security = string.Equals(current.StyleId, CharacterCreationLifestyleStyleIds.Standard, StringComparison.Ordinal)
                ? 0 : Math.Min(current.Security, option.MaximumSecurity),
            BonusLifestylePoints = option.AllowsBonusLifestylePoints
                ? current.BonusLifestylePoints : 0,
            Qualities = current.Qualities.Where(quality => !quality.IsBuiltIn).ToArray()
        };
        return true;
    }

    public bool TrySetName(CharacterCreationLifestylesInteractionState state, string? value)
        => TrySetText(state, value, current => current with { Name = value! }, requireNonEmpty: true);

    public bool TrySetCity(CharacterCreationLifestylesInteractionState state, string? value)
        => TrySetText(state, value, current => current with { City = value! });

    public bool TrySetDistrict(CharacterCreationLifestylesInteractionState state, string? value)
        => TrySetText(state, value, current => current with { District = value! });

    public bool TrySetBorough(CharacterCreationLifestylesInteractionState state, string? value)
        => TrySetText(state, value, current => current with { Borough = value! });

    public bool TrySetStyle(CharacterCreationLifestylesInteractionState state, string styleId)
    {
        if (!TryCurrent(state, out CharacterCreationLifestyleConfiguration current)
            || !CharacterCreationLifestyleStyleIds.All.Contains(styleId))
        {
            return false;
        }
        bool standard = string.Equals(styleId, CharacterCreationLifestyleStyleIds.Standard, StringComparison.Ordinal);
        _current = current with
        {
            StyleId = styleId,
            Area = standard ? 0 : current.Area,
            Comforts = standard ? 0 : current.Comforts,
            Security = standard ? 0 : current.Security,
            BonusLifestylePoints = standard ? 0 : current.BonusLifestylePoints
        };
        return true;
    }

    public bool TrySetIncrements(CharacterCreationLifestylesInteractionState state, int value)
        => TrySetInt(state, value, 1, 1200, current => current with { Increments = value });

    public bool TrySetPercentage(CharacterCreationLifestylesInteractionState state, decimal value)
    {
        if (!TryCurrent(state, out CharacterCreationLifestyleConfiguration current)
            || value <= 0m || value > 1000m)
        {
            return false;
        }
        _current = current with { Percentage = value };
        return true;
    }

    public bool TrySetRoommates(CharacterCreationLifestylesInteractionState state, int value)
        => TrySetInt(state, value, 0, 100, current => current with
        {
            Roommates = value,
            SplitCostWithRoommates = value > 0 && current.SplitCostWithRoommates
        });

    public bool TrySetSplit(CharacterCreationLifestylesInteractionState state, bool value)
    {
        if (!TryCurrent(state, out CharacterCreationLifestyleConfiguration current)
            || value && (current.Roommates == 0 || current.TrustFund))
        {
            return false;
        }
        _current = current with { SplitCostWithRoommates = value };
        return true;
    }

    public bool TrySetTrustFund(CharacterCreationLifestylesInteractionState state, bool value)
    {
        if (!TryCurrent(state, out CharacterCreationLifestyleConfiguration current))
            return false;
        _current = current with
        {
            TrustFund = value,
            Roommates = value ? 0 : current.Roommates,
            SplitCostWithRoommates = value ? false : current.SplitCostWithRoommates
        };
        return true;
    }

    public bool TrySetArea(CharacterCreationLifestylesInteractionState state, int value)
        => TrySetAspect(state, value, static (current, selected) => current with { Area = selected });

    public bool TrySetComforts(CharacterCreationLifestylesInteractionState state, int value)
        => TrySetAspect(state, value, static (current, selected) => current with { Comforts = selected });

    public bool TrySetSecurity(CharacterCreationLifestylesInteractionState state, int value)
        => TrySetAspect(state, value, static (current, selected) => current with { Security = selected });

    public bool TrySetBonusLifestylePoints(CharacterCreationLifestylesInteractionState state, int value)
    {
        if (!TryCurrent(state, out CharacterCreationLifestyleConfiguration current)
            || CreationLifestylesPhoneAuthority.ResolveUniqueSelectableOption(
                state,
                current.BaseLifestyleOptionId) is not { AllowsBonusLifestylePoints: true }
            || value is < 0 or > 100)
        {
            return false;
        }
        _current = current with { BonusLifestylePoints = value };
        return true;
    }

    public bool TryToggleQuality(
        CharacterCreationLifestylesInteractionState state,
        string optionId,
        bool selected)
    {
        if (!TryCurrent(state, out CharacterCreationLifestyleConfiguration current)
            || CreationLifestylesPhoneAuthority.ResolveUniqueSelectableQuality(state, optionId) is null)
        {
            return false;
        }
        CharacterCreationLifestyleQualitySelection[] retained = current.Qualities
            .Where(item => item.IsBuiltIn
                || !string.Equals(item.OptionId, optionId, StringComparison.Ordinal))
            .ToArray();
        if (!selected)
        {
            _current = current with { Qualities = retained };
            return true;
        }
        _current = current with
        {
            Qualities = retained.Append(new CharacterCreationLifestyleQualitySelection(
                    DeterministicQualityIdentity(current.LifestyleId, optionId),
                    optionId,
                    Extra: string.Empty,
                    UseLifestylePoints: false,
                    IsFree: false,
                    IsBuiltIn: false))
                .OrderBy(item => item.InstanceId)
                .ToArray()
        };
        return true;
    }

    public bool HasChanges(CharacterCreationLifestylesInteractionState state)
        => TryCurrent(state, out CharacterCreationLifestyleConfiguration current)
           && (_original is null || !ConfigurationEquals(_original, current));

    public CharacterCreationLifestyleMutationInput? ToInput(
        CharacterCreationLifestylesInteractionState state)
        => TryCurrent(state, out CharacterCreationLifestyleConfiguration current)
           && HasChanges(state)
            ? new CharacterCreationLifestyleMutationInput(_mutationKind, current.LifestyleId, current)
            : null;

    public CharacterCreationLifestyleMutationInput? ToDeleteInput(
        CharacterCreationLifestylesInteractionState state)
        => TryCurrent(state, out CharacterCreationLifestyleConfiguration current)
           && string.Equals(_mutationKind, CharacterCreationLifestyleMutationKinds.Edit, StringComparison.Ordinal)
            ? new CharacterCreationLifestyleMutationInput(
                CharacterCreationLifestyleMutationKinds.Delete,
                current.LifestyleId,
                Configuration: null)
            : null;

    private bool TrySetText(
        CharacterCreationLifestylesInteractionState state,
        string? value,
        Func<CharacterCreationLifestyleConfiguration, CharacterCreationLifestyleConfiguration> update,
        bool requireNonEmpty = false)
    {
        value ??= string.Empty;
        if (!TryCurrent(state, out CharacterCreationLifestyleConfiguration current)
            || requireNonEmpty && value.Length == 0
            || value.Length > 1024
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || value.Any(character => char.IsControl(character)
                && character is not '\r' and not '\n' and not '\t'))
        {
            return false;
        }
        _current = update(current);
        return true;
    }

    private bool TrySetInt(
        CharacterCreationLifestylesInteractionState state,
        int value,
        int minimum,
        int maximum,
        Func<CharacterCreationLifestyleConfiguration, CharacterCreationLifestyleConfiguration> update)
    {
        if (!TryCurrent(state, out CharacterCreationLifestyleConfiguration current)
            || value < minimum || value > maximum)
        {
            return false;
        }
        _current = update(current);
        return true;
    }

    private bool TrySetAspect(
        CharacterCreationLifestylesInteractionState state,
        int value,
        Func<CharacterCreationLifestyleConfiguration, int, CharacterCreationLifestyleConfiguration> update)
    {
        if (!TryCurrent(state, out CharacterCreationLifestyleConfiguration current)
            || string.Equals(current.StyleId, CharacterCreationLifestyleStyleIds.Standard, StringComparison.Ordinal)
            || value is < 0 or > 100)
        {
            return false;
        }
        _current = update(current, value);
        return true;
    }

    private bool TryCurrent(
        CharacterCreationLifestylesInteractionState state,
        out CharacterCreationLifestyleConfiguration current)
    {
        current = _current!;
        return _current is not null
               && Matches(state, _current.LifestyleId, _mutationKind);
    }

    private static Guid DeterministicQualityIdentity(Guid lifestyleId, string optionId)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            "chummer.android.creation-lifestyle-quality/v1\0"
            + lifestyleId.ToString("D") + "\0" + optionId));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static bool ConfigurationEquals(
        CharacterCreationLifestyleConfiguration left,
        CharacterCreationLifestyleConfiguration right)
        => left with { Qualities = [] } == right with { Qualities = [] }
           && left.Qualities.OrderBy(item => item.InstanceId)
               .SequenceEqual(right.Qualities.OrderBy(item => item.InstanceId));
}

internal static class CreationLifestylesPhoneAuthority
{
    public static bool IsBound(
        CharacterCreationLifestylesInteractionState state,
        CharacterOverviewState overview)
        => overview.Profile?.Created == false
           && overview.WorkspaceId is { } workspaceId
           && overview.CreationWizard is { CharacterCreated: false } wizard
           && state.Binding.WorkspaceId == workspaceId
           && state.Binding.WorkspaceRevision == overview.ContentRevision
           && state.Binding.ContentRevision == overview.ContentRevision
           && state.Binding.SavedRevision == overview.SavedRevision
           && string.Equals(state.Binding.ContentDigest, wizard.ContentDigest, StringComparison.Ordinal)
           && string.Equals(wizard.WorkspaceId, workspaceId.Value, StringComparison.Ordinal)
           && wizard.WorkspaceRevision == overview.ContentRevision
           && IsDigest(state.SnapshotDigest)
           && IsDigest(state.Binding.ContentDigest)
           && !string.IsNullOrWhiteSpace(state.Binding.AuxiliaryStateDigest)
           && IsDigest(state.Binding.SourceDigest)
           && IsDigest(state.Binding.RulesDigest)
           && IsDigest(state.Binding.RuntimeDigest)
           && state.Blockers.All(blocker => !string.IsNullOrWhiteSpace(blocker))
           && state.Lifestyles.All(IsExactProjection)
           && state.Lifestyles.Select(item => item.Configuration.LifestyleId).Distinct().Count()
                == state.Lifestyles.Count;

    public static bool IsReady(
        CharacterCreationLifestylesInteractionState state,
        CharacterOverviewState overview)
        => IsBound(state, overview)
           && state.CanEdit
           && state.Blockers.Count == 0
           && state.Budget.IsExact
           && state.Budget.Blockers.Count == 0
           && state.Authority.IsAuthoritative
           && state.Authority.Blockers.Count == 0
           && state.Authority.LifestyleOptions.Any(option =>
               option.IsSelectable && option.EligibilityIsExact && option.Blockers.Count == 0);

    public static CharacterCreationLifestyleCatalogOption? ResolveUniqueSelectableOption(
        CharacterCreationLifestylesInteractionState state,
        string optionId)
    {
        CharacterCreationLifestyleCatalogOption[] matches = state.Authority.LifestyleOptions
            .Where(option => string.Equals(option.OptionId, optionId, StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        return matches is [{ IsSelectable: true, EligibilityIsExact: true } option]
               && option.Blockers.Count == 0
            ? option
            : null;
    }

    public static CharacterCreationLifestyleQualityCatalogOption? ResolveUniqueSelectableQuality(
        CharacterCreationLifestylesInteractionState state,
        string optionId)
    {
        CharacterCreationLifestyleQualityCatalogOption[] matches = state.Authority.QualityOptions
            .Where(option => string.Equals(option.OptionId, optionId, StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        return matches is [{ IsSelectable: true, EligibilityIsExact: true } option]
               && option.Blockers.Count == 0
            ? option
            : null;
    }

    public static CharacterCreationLifestyleProjection? ResolveUniqueLifestyle(
        CharacterCreationLifestylesInteractionState state,
        Guid lifestyleId)
    {
        CharacterCreationLifestyleProjection[] matches = state.Lifestyles
            .Where(item => item.Configuration.LifestyleId == lifestyleId)
            .Take(2)
            .ToArray();
        return matches is [var projection] && IsExactProjection(projection)
            ? projection
            : null;
    }

    public static bool IsExactProjection(CharacterCreationLifestyleProjection projection)
        => projection.Configuration.LifestyleId != Guid.Empty
           && projection.SourceId != Guid.Empty
           && IsDigest(projection.LifestyleDigest)
           && string.Equals(
               projection.LifestyleDigest,
               CharacterCreationLifestylesRules.ComputeProjectionDigest(projection),
               StringComparison.Ordinal);

    public static bool PreparedMatches(
        CharacterCreationLifestylePreparedPreview prepared,
        CharacterCreationLifestylesInteractionState state,
        CharacterOverviewState overview)
        => IsReady(state, overview)
           && BindingEquals(prepared.Binding, state.Binding)
           && string.Equals(prepared.LifestylesSnapshotDigest, state.SnapshotDigest, StringComparison.Ordinal)
           && prepared.LifestylesBefore.Count == state.Lifestyles.Count
           && prepared.LifestylesBefore.All(before => state.Lifestyles.Any(current =>
               current.Configuration.LifestyleId == before.Configuration.LifestyleId
               && string.Equals(current.LifestyleDigest, before.LifestyleDigest, StringComparison.Ordinal)))
           && prepared.Mutation.LifestyleId != Guid.Empty
           && prepared.RequiresExplicitConfirmation
           && prepared.CanConfirm
           && prepared.Blockers.Count == 0
           && prepared.WritePlan.PreservesUntouchedSiblingState
           && prepared.WritePlan.PreservesNestedState
           && string.Equals(prepared.WritePlan.ContentDigestBefore, state.Binding.ContentDigest, StringComparison.Ordinal)
           && string.Equals(
               prepared.WritePlan.PlanDigest,
               CharacterCreationLifestylesRules.ComputePlanDigest(prepared.WritePlan),
               StringComparison.Ordinal)
           && string.Equals(
               prepared.PreviewDigest,
               CharacterCreationLifestylesRules.ComputePreviewDigest(new CharacterCreationLifestylePreview(
                   CharacterCreationLifestylesSchemas.PreviewV1,
                   CharacterCreationWizardStepIds.ContactsLifestyles,
                   prepared.Binding,
                   prepared.Mutation.MutationKind,
                   prepared.Before,
                   prepared.After,
                   prepared.BudgetBefore,
                   prepared.BudgetAfter,
                   prepared.WritePlan,
                   prepared.Blockers,
                   prepared.RequiresExplicitConfirmation,
                   prepared.CanConfirm,
                   prepared.PreviewDigest)),
               StringComparison.Ordinal)
           && prepared.IdempotencyKey is { Length: > 0 and <= 200 };

    public static bool ReceiptMatches(
        CharacterCreationLifestylePreparedPreview prepared,
        CharacterCreationLifestyleReceipt receipt)
        => receipt.WorkspaceId == prepared.Binding.WorkspaceId
           && receipt.LifestyleId == prepared.Mutation.LifestyleId
           && string.Equals(receipt.MutationKind, prepared.Mutation.MutationKind, StringComparison.Ordinal)
           && receipt.PreviousWorkspaceRevision == prepared.Binding.WorkspaceRevision
           && receipt.WorkspaceRevision == receipt.PreviousWorkspaceRevision + 1
           && receipt.PreviousContentRevision == prepared.Binding.ContentRevision
           && receipt.ContentRevision == receipt.PreviousContentRevision + 1
           && receipt.SavedRevision == receipt.ContentRevision
           && string.Equals(receipt.ContentDigestBefore, prepared.Binding.ContentDigest, StringComparison.Ordinal)
           && string.Equals(receipt.ContentDigestAfter, prepared.WritePlan.ContentDigestAfter, StringComparison.Ordinal)
           && IsDigest(receipt.ReceiptDigest)
           && string.Equals(receipt.ReceiptDigest,
               CharacterCreationLifestylesRules.ComputeReceiptDigest(receipt), StringComparison.Ordinal);

    public static bool RefreshedStateMatches(
        CharacterCreationLifestylePreparedPreview prepared,
        CharacterCreationLifestyleReceipt receipt,
        CharacterCreationLifestylesInteractionState refreshed,
        CharacterOverviewState overview)
        => IsReady(refreshed, overview)
           && refreshed.Binding.WorkspaceId == receipt.WorkspaceId
           && refreshed.Binding.WorkspaceRevision == receipt.WorkspaceRevision
           && refreshed.Binding.ContentRevision == receipt.ContentRevision
           && refreshed.Binding.SavedRevision == receipt.SavedRevision
           && string.Equals(refreshed.Binding.ContentDigest, receipt.ContentDigestAfter, StringComparison.Ordinal)
           && refreshed.Budget.Used == prepared.BudgetAfter.Used
           && refreshed.Budget.Remaining == prepared.BudgetAfter.Remaining
           && (prepared.After is null
               ? refreshed.Lifestyles.All(item => item.Configuration.LifestyleId != receipt.LifestyleId)
               : refreshed.Lifestyles.Any(item =>
                   item.Configuration.LifestyleId == receipt.LifestyleId
                   && string.Equals(item.LifestyleDigest, prepared.After.LifestyleDigest, StringComparison.Ordinal)));

    public static bool BindingEquals(
        CharacterCreationLifestyleBinding left,
        CharacterCreationLifestyleBinding right)
        => left == right;

    private static bool IsDigest(string? value)
        => value is { Length: 71 }
           && value.StartsWith("sha256:", StringComparison.Ordinal)
           && value.AsSpan(7).ToString().All(character =>
               character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
