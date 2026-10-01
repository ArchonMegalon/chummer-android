using Chummer.Application.Characters;
using Chummer.Contracts.Characters;

namespace Chummer.Android.Native;

internal sealed record CreationKarmaQualityPage(
    IReadOnlyList<CharacterCreationQualityCatalogOption> Options, int? NextOffset);

public sealed partial class RunnerSessionCoordinator
{
    // Read-only, bounded display page. A candidate uses the same full Core
    // preview as an actual selection, including replacement ratings and the
    // shared Karma budget. Browsing never issues/replaces a confirmation.
    internal Task<CreationKarmaQualityPage?> LoadCreationKarmaQualityPageAsync(
        CharacterCreationKarmaMetatypeState state, CreationKarmaPhoneSelection selection,
        string search, int offset, int pageSize, CancellationToken ct, Func<bool> isCurrentPage)
    {
        var frozen = selection.Freeze();
        if (offset is < 0 or > 65_536 || pageSize is < 1 or > 20) throw new ArgumentOutOfRangeException(nameof(offset));
        return WithWorkspaceActivationGateAsync(async () =>
        {
            if (!isCurrentPage() || !IsCreationKarmaStateCurrent(state)
                || !_karmaStates.TryGetValue(state, out var original)
                || original.DisplayOwnerContext is not { IsValid: true } owner
                || _ownerBoundKarmaService is not { } service
                || state.QualitiesCatalog is not { } catalog || frozen.QualityOptionIds is not { } selected)
                return null;
            var result = await Task.Run<CreationKarmaQualityPage?>(() =>
            {
                ct.ThrowIfCancellationRequested();
                // Filtering, culture-aware sorting and indexing visit the whole
                // catalog. Keep them off the phone UI context, not just Core reads.
                var candidates = catalog.Options.Where(option => option.IsSelectable
                        && !selected.Contains(option.OptionId, StringComparer.Ordinal)
                        && option.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase))
                    .OrderBy(option => option.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(option => option.Rating).ThenBy(option => option.OptionId, StringComparer.Ordinal).ToArray();
                var byId = catalog.Options.ToDictionary(option => option.OptionId, StringComparer.Ordinal);
                ct.ThrowIfCancellationRequested();
                var checkedOptions = new List<CharacterCreationQualityCatalogOption>();
                var selections = new List<IReadOnlyList<string>>();
                int next = offset;
                int end = Math.Min(candidates.Length, offset + pageSize * 3);
                for (int index = offset; index < end && checkedOptions.Count < pageSize; index++)
                {
                    ct.ThrowIfCancellationRequested();
                    var option = candidates[index];
                    next = index + 1;
                    // Core can reject unsupported source effects without a
                    // full reload of every other creation authority. Bound the
                    // expensive previews, not just the number of visible rows.
                    if (!CharacterCreationKarmaQualitiesRules.IsExactPurchase(option)) continue;
                    var ids = selected.Where(id => byId[id].SelectionKey != option.SelectionKey)
                        .Append(option.OptionId).ToArray();
                    checkedOptions.Add(option);
                    selections.Add(ids);
                }
                if (checkedOptions.Count == 0)
                    return new(Array.Empty<CharacterCreationQualityCatalogOption>(), next < candidates.Length ? next : null);
                // Core admits one fresh source/workspace snapshot for this bounded
                // read, but computes the complete preview of every candidate.
                var batch = service.PreviewQualitySelections(owner, new(state.Binding, frozen.MetatypeOptionId,
                    selections, frozen.TalentOptionId, frozen.Attributes, frozen.Skills, frozen.ResourceKarmaInvestment,
                    frozen.GearSelections, frozen.ContactSelections, frozen.LifestyleSelections,
                    frozen.StartingLifestyleId, frozen.MagicSelections), ct);
                ct.ThrowIfCancellationRequested();
                if (batch is not { Outcome: CharacterCreationFoundationOutcomes.Success, Value: { } previews }
                    || previews.Results.Count != checkedOptions.Count) return null;
                var available = new List<CharacterCreationQualityCatalogOption>();
                for (int index = 0; index < checkedOptions.Count; index++)
                {
                    ct.ThrowIfCancellationRequested();
                    var option = checkedOptions[index];
                    var preview = previews.Results[index];
                    if (preview.Blockers.Contains(CharacterCreationKarmaMetatypeBlockers.StaleBinding)
                        || preview.Outcome == CharacterCreationFoundationOutcomes.Conflict)
                        return null;
                    if (preview is not { Outcome: CharacterCreationFoundationOutcomes.Success, Value: { } quote })
                    {
                        // Only a known candidate rejection is an unavailable
                        // option. An authority/read failure is not an empty list.
                        if (preview.Blockers.Count > 0 && preview.Blockers.All(blocker => blocker is
                            CharacterCreationQualitiesBlockers.InvalidSelection or
                            CharacterCreationKarmaMetatypeBlockers.BudgetExceeded or
                            CharacterCreationSkillsBlockers.AllocationInvalid)) continue;
                        return null;
                    }
                    if (quote.Binding != state.Binding || quote.SnapshotDigest != state.SnapshotDigest)
                        return null;
                    // Like the Priority picker, permit building metagenic pairs
                    // one choice at a time; final confirmation still requires balance.
                    if (quote.Qualities is null || !quote.KarmaBudget.IsExact
                        || quote.Blockers.Any(blocker => blocker != CharacterCreationQualitiesBlockers.MetagenicImbalanced))
                        continue;
                    available.Add(option);
                }
                return new(Array.AsReadOnly(available.ToArray()), next < candidates.Length ? next : null);
            }, ct);
            ct.ThrowIfCancellationRequested();
            return isCurrentPage() && IsCreationKarmaStateCurrent(state) ? result : null;
        }, ct);
    }
}
