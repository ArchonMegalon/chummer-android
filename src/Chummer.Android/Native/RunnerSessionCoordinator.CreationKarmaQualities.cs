using Chummer.Application.Characters;
using Chummer.Contracts.Characters;

namespace Chummer.Android.Native;

internal sealed record CreationKarmaQualityPage(
    IReadOnlyList<CharacterCreationQualityCatalogOption> Options, int? NextOffset,
    CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeQuote>? DraftPreview = null,
    IReadOnlyDictionary<string, string>? Summaries = null,
    IReadOnlyList<CreationQualityCatalogGroup>? Groups = null);

internal sealed record CreationQualityCatalogGroup(CharacterCreationQualityCatalogOption[] Members,
    CharacterCreationQualityCatalogOption[] Available);

// Only exact catalog identities form a rating family. Ambiguous duplicate
// ratings remain separate offers; display names never establish replacement.
internal static class CreationQualityCatalogRatings
{
    internal static IReadOnlyList<CharacterCreationQualityCatalogOption[]> Group(
        IEnumerable<CharacterCreationQualityCatalogOption> options) => options
        .GroupBy(option => (option.SourceId, option.SelectionKey, option.Type, option.FollowUpChoiceId,
            option.IsMetagenic, option.IsFreeOrGranted, option.CountsAgainstKarma,
            option.CountsAgainstQualityLimit, option.MaximumSelections, option.SourceNodeDigest, option.ResolvedArmCount))
        .SelectMany(group => group.Key.SourceId == Guid.Empty || string.IsNullOrWhiteSpace(group.Key.SelectionKey)
            || group.GroupBy(option => option.Rating).Any(ratings => ratings.Count() > 1)
                ? group.Select(option => new[] { option })
                : new[] { group.OrderBy(option => option.Rating).ToArray() }).ToArray();

    internal static IReadOnlyList<string> Replace(IReadOnlyList<string> selected,
        IEnumerable<CharacterCreationQualityCatalogOption> catalog, CharacterCreationQualityCatalogOption option)
    {
        var family = Group(catalog).Single(group => group.Any(member => member.OptionId == option.OptionId));
        var replaced = family.Select(member => member.OptionId).ToHashSet(StringComparer.Ordinal);
        return selected.Where(id => !replaced.Contains(id)).Append(option.OptionId).ToArray();
    }
}

public sealed partial class RunnerSessionCoordinator
{
    // Read-only, bounded display page. A candidate uses the same full Core
    // preview as an actual selection, including replacement ratings and the
    // shared Karma budget. Browsing never issues/replaces a confirmation.
    internal Task<CreationKarmaQualityPage?> LoadCreationKarmaQualityPageAsync(
        CharacterCreationKarmaMetatypeState state, CreationKarmaPhoneSelection selection,
        string search, int offset, int pageSize, CancellationToken ct, Func<bool> isCurrentPage)
        => PrepareCreationKarmaQualityPageAsync(state, selection, search, offset, pageSize, ct, isCurrentPage, previewDraft: false);

    // Explicit draft review on appearance/change, distinct from read-only browsing.
    // The first batch member is the exact draft; candidate quotes are never issued.
    internal Task<CreationKarmaQualityPage?> PreviewCreationKarmaQualityPageAsync(
        CharacterCreationKarmaMetatypeState state, CreationKarmaPhoneSelection selection,
        string search, int offset, int pageSize, CancellationToken ct, Func<bool> isCurrentPage)
        => PrepareCreationKarmaQualityPageAsync(state, selection, search, offset, pageSize, ct, isCurrentPage, previewDraft: true);

    private Task<CreationKarmaQualityPage?> PrepareCreationKarmaQualityPageAsync(
        CharacterCreationKarmaMetatypeState state, CreationKarmaPhoneSelection selection,
        string search, int offset, int pageSize, CancellationToken ct, Func<bool> isCurrentPage, bool previewDraft)
    {
        var frozen = selection.Freeze();
        if (offset is < 0 or > 65_536 || pageSize < 1 || pageSize > (previewDraft ? 19 : 20))
            throw new ArgumentOutOfRangeException(nameof(offset));
        return WithWorkspaceActivationGateAsync(async () =>
        {
            if (!isCurrentPage() || !IsCreationKarmaStateCurrent(state)
                || !_karmaStates.TryGetValue(state, out var original)
                || original.DisplayOwnerContext is not { IsValid: true } owner
                || _ownerBoundKarmaService is not { } service
                || state.QualitiesCatalog is not { } catalog || frozen.QualityOptionIds is not { } selected)
                return null;
            if (previewDraft) _karmaCurrentReview = null;
            var result = await Task.Run<CreationKarmaQualityPage?>(() =>
            {
                ct.ThrowIfCancellationRequested();
                // Filtering, culture-aware sorting and indexing visit the whole
                // catalog. Keep them off the phone UI context, not just Core reads.
                var byId = catalog.Options.ToDictionary(option => option.OptionId, StringComparer.Ordinal);
                var families = CreationQualityCatalogRatings.Group(byId.Values);
                var candidates = families.Where(group => group.Any(option => option.IsSelectable
                            && !selected.Contains(option.OptionId, StringComparer.Ordinal))
                        && (group.Any(option => selected.Contains(option.OptionId, StringComparer.Ordinal))
                            || group.Any(option => option.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase))))
                    .OrderByDescending(group => group.Any(option => selected.Contains(option.OptionId, StringComparer.Ordinal)))
                    .ThenBy(group => group[0].Name, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(group => group[0].OptionId, StringComparer.Ordinal).ToArray();
                ct.ThrowIfCancellationRequested();
                var checkedOptions = new List<CharacterCreationQualityCatalogOption>();
                var checkedFamilies = new List<CharacterCreationQualityCatalogOption[]>();
                var selections = new List<IReadOnlyList<string>>();
                if (previewDraft) selections.Add(selected);
                int next = offset;
                int end = Math.Min(candidates.Length, offset + pageSize * 3);
                int checkedGroups = 0;
                for (int index = offset; index < end && checkedGroups < pageSize; index++)
                {
                    ct.ThrowIfCancellationRequested();
                    var family = candidates[index];
                    // Core can reject unsupported source effects without a
                    // full reload of every other creation authority. Bound the
                    // expensive previews, not just the number of visible rows.
                    var exact = family.Where(option => option.IsSelectable
                        && CharacterCreationKarmaQualitiesRules.IsExactPurchase(option)).ToArray();
                    if (exact.Length == 0) { next = index + 1; continue; }
                    // Normally at most 20 candidate previews per page. A single
                    // larger family stays intact: ExactPurchase admits only unique
                    // canonical ratings 1..100, so the hard bound is 100 (+draft).
                    // Defer a subsequent family instead of truncating its ratings.
                    if (checkedOptions.Count > 0 && checkedOptions.Count + exact.Length > 20) break;
                    if (exact.Length > 100) return null;
                    next = index + 1;
                    checkedGroups++;
                    checkedFamilies.Add(family);
                    var replaced = family.Select(option => option.OptionId).ToHashSet(StringComparer.Ordinal);
                    foreach (var option in exact)
                    {
                        checkedOptions.Add(option);
                        selections.Add(selected.Where(id => !replaced.Contains(id)).Append(option.OptionId).ToArray());
                    }
                }
                // Core admits one fresh source/workspace snapshot for this bounded
                // batch and computes every exact member. Do not split a family's
                // visible ratings across pages; Core's per-batch bound remains 20.
                var previews = new List<CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeQuote>>();
                foreach (var chunk in selections.Chunk(20))
                {
                    ct.ThrowIfCancellationRequested();
                    var batch = service.PreviewQualitySelections(owner, new(state.Binding, frozen.MetatypeOptionId,
                        chunk, frozen.TalentOptionId, frozen.Attributes, frozen.Skills, frozen.ResourceKarmaInvestment,
                        frozen.GearSelections, frozen.ContactSelections, frozen.LifestyleSelections,
                        frozen.StartingLifestyleId, frozen.MagicSelections), ct);
                    ct.ThrowIfCancellationRequested();
                    if (batch is not { Outcome: CharacterCreationFoundationOutcomes.Success, Value: { } batchPreview }
                        || batchPreview.Results.Count != chunk.Length) return null;
                    previews.AddRange(batchPreview.Results);
                }
                var draft = previewDraft ? previews[0] : null;
                var available = new List<CharacterCreationQualityCatalogOption>();
                for (int index = 0; index < checkedOptions.Count; index++)
                {
                    ct.ThrowIfCancellationRequested();
                    var option = checkedOptions[index];
                    var preview = previews[index + (previewDraft ? 1 : 0)];
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
                // Source XML parsing/hash checks belong to this background batch,
                // never to a native row render or picker/search callback.
                var summaries = available.Concat(selected.Where(byId.ContainsKey).Select(id => byId[id]))
                    .DistinctBy(option => option.OptionId).ToDictionary(option => option.OptionId,
                        option => CreationQualityInfo.Effects(option.SourceNodeXml, option.Rating).FirstOrDefault() ?? string.Empty,
                        StringComparer.Ordinal);
                var admitted = available.Select(option => option.OptionId).ToHashSet(StringComparer.Ordinal);
                var groups = checkedFamilies.Select(family => new CreationQualityCatalogGroup(family,
                    family.Where(option => admitted.Contains(option.OptionId)).ToArray())).ToArray();
                var offers = available.Where(option => !selected.Contains(option.OptionId, StringComparer.Ordinal)).ToArray();
                return new(Array.AsReadOnly(offers), next < candidates.Length ? next : null, draft, summaries, groups);
            }, ct);
            ct.ThrowIfCancellationRequested();
            if (!isCurrentPage() || !IsCreationKarmaStateCurrent(state) || result is null) return null;
            if (!previewDraft) return result;
            if (result.DraftPreview is not { } draftPreview) return null;
            var accepted = AcceptKarmaPreview(draftPreview, state, frozen, original);
            if (accepted.Outcome == CharacterCreationFoundationOutcomes.Conflict
                || accepted.Blockers.Contains(CharacterCreationKarmaMetatypeBlockers.StaleBinding)) return null;
            return result with { DraftPreview = accepted };
        }, ct);
    }
}
