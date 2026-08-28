using Chummer.Contracts.Characters;

namespace Chummer.Android.Native;

/// <summary>
/// Rule-free phone projection of Core's whole-build finalization authority.
/// It never promotes a wizard snapshot to readiness and never infers a missing
/// draft.  Its only purpose is to keep Core blockers, source anchors, and the
/// exact CAS binding visible while the user works through the specialized
/// creation pages.
/// </summary>
public sealed record CreationPriorityLegalPathStep(
    string StepId,
    bool IsRequired,
    bool IsComplete,
    string? DraftDigest,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> SourceAnchorIds);

public sealed record CreationPriorityLegalPathProjection(
    string Outcome,
    bool CanOpenReview,
    long? ContentRevision,
    long? SavedRevision,
    string? SnapshotDigest,
    IReadOnlyList<CreationPriorityLegalPathStep> Steps,
    IReadOnlyList<string> Blockers)
{
    public static CreationPriorityLegalPathProjection Loading { get; } = new(
        Outcome: "loading",
        CanOpenReview: false,
        ContentRevision: null,
        SavedRevision: null,
        SnapshotDigest: null,
        Steps: [],
        Blockers: []);

    public static CreationPriorityLegalPathProjection From(
        CharacterCreationFinalizationResult<CharacterCreationFinalizationState>? result)
    {
        if (result is null)
            return Loading;

        CharacterCreationFinalizationState? state = result.Value;
        if (state is null)
        {
            return new CreationPriorityLegalPathProjection(
                result.Outcome,
                CanOpenReview: false,
                ContentRevision: null,
                SavedRevision: null,
                SnapshotDigest: null,
                Steps: [],
                Blockers: Normalize(result.Blockers, result.Outcome));
        }

        CreationPriorityLegalPathStep[] steps = state.Steps
            .Select(static step => new CreationPriorityLegalPathStep(
                step.StepId,
                step.IsRequired,
                step.IsComplete,
                step.DraftDigest,
                step.Blockers.Distinct(StringComparer.Ordinal).ToArray(),
                step.SourceAnchorIds.Distinct(StringComparer.Ordinal).ToArray()))
            .ToArray();
        string[] blockers = result.Blockers
            .Concat(state.Blockers)
            .Concat(steps.Where(static step => step.IsRequired && !step.IsComplete)
                .SelectMany(static step => step.Blockers))
            .Where(static blocker => !string.IsNullOrWhiteSpace(blocker))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        bool exactAvailable = string.Equals(
            result.Outcome,
            CharacterCreationFinalizationOutcomes.Available,
            StringComparison.Ordinal);
        bool canOpenReview = exactAvailable
                             && state.CanReview
                             && blockers.Length == 0
                             && steps.Where(static step => step.IsRequired)
                                 .All(static step => step.IsComplete);
        return new CreationPriorityLegalPathProjection(
            result.Outcome,
            canOpenReview,
            state.Binding.ContentRevision,
            state.Binding.SavedRevision,
            state.SnapshotDigest,
            steps,
            blockers);
    }

    private static string[] Normalize(IReadOnlyList<string> blockers, string outcome)
    {
        string[] normalized = blockers
            .Where(static blocker => !string.IsNullOrWhiteSpace(blocker))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return normalized.Length > 0
            ? normalized
            : [string.IsNullOrWhiteSpace(outcome)
                ? CharacterCreationFinalizationOutcomes.Unavailable
                : outcome];
    }
}
