using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

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

    public static string? NormalizeMachineDigestPayload(string? value)
        => CharacterCreationFinalizationDigest.IsCanonical(value)
            ? value!["sha256:".Length..]
            : null;

    public static CharacterCreationFinalizationReceipt? ResolvePersistedPriorityReceipt(
        CharacterCreationFinalizationResult<CharacterCreationFinalizationState>? result,
        CharacterWorkspaceId workspaceId,
        long contentRevision,
        long savedRevision)
    {
        if (contentRevision <= 0
            || savedRevision <= 0
            || savedRevision != contentRevision
            || result is null
            || !string.Equals(
                result.Outcome,
                CharacterCreationFinalizationOutcomes.Blocked,
                StringComparison.Ordinal)
            || result.Blockers.All(static blocker => !string.Equals(
                blocker,
                CharacterCreationFinalizationBlockers.CharacterAlreadyCreated,
                StringComparison.Ordinal))
            || result.Value is not
               {
                   Schema: CharacterCreationFinalizationSchemas.StateV1,
                   CharacterCreated: true,
                   CanReview: false,
                   LastReceipt: { } receipt
               } state
            || !string.Equals(
                receipt.Schema,
                CharacterCreationFinalizationSchemas.ReceiptV1,
                StringComparison.Ordinal)
            || state.Binding.WorkspaceId != workspaceId
            || state.Binding.ContentRevision != contentRevision
            || state.Binding.SavedRevision != savedRevision
            || receipt.WorkspaceId != workspaceId
            || receipt.ContentRevision != contentRevision
            || receipt.SavedRevision != savedRevision
            || receipt.SavedRevision != receipt.ContentRevision
            || receipt.PreviousContentRevision + 1 != receipt.ContentRevision
            || !receipt.CharacterCreated
            || !CharacterCreationFinalizationDigest.IsCanonical(state.SnapshotDigest)
            || !CharacterCreationFinalizationDigest.EqualsFixedTime(
                state.SnapshotDigest,
                CharacterCreationFinalizationDigest.Compute(
                    state with { SnapshotDigest = string.Empty }))
            || !CharacterCreationFinalizationDigest.IsCanonical(
                state.Binding.RawCharacterXmlDigest)
            || !CharacterCreationFinalizationDigest.IsCanonical(receipt.RawCharacterXmlDigest)
            || !CharacterCreationFinalizationDigest.EqualsFixedTime(
                state.Binding.RawCharacterXmlDigest,
                receipt.RawCharacterXmlDigest)
            || !CharacterCreationFinalizationDigest.IsCanonical(
                state.Binding.AuthorityDigest)
            || !CharacterCreationFinalizationDigest.IsCanonical(receipt.AuthorityDigest)
            || !CharacterCreationFinalizationDigest.EqualsFixedTime(
                state.Binding.AuthorityDigest,
                receipt.AuthorityDigest)
            || !string.Equals(
                state.Binding.BuildMethod,
                CharacterCreationBuildMethods.Priority,
                StringComparison.Ordinal)
            || !string.Equals(
                receipt.BuildMethod,
                CharacterCreationBuildMethods.Priority,
                StringComparison.Ordinal)
            || !CharacterCreationFinalizationDigest.IsCanonical(receipt.PlanDigest)
            || !CharacterCreationFinalizationDigest.IsCanonical(receipt.PreviewDigest)
            || !CharacterCreationFinalizationDigest.IsCanonical(receipt.ReceiptDigest)
            || !CharacterCreationFinalizationDigest.EqualsFixedTime(
                receipt.ReceiptDigest,
                CharacterCreationFinalizationDigest.ComputeReceiptDigest(receipt)))
        {
            return null;
        }
        return receipt;
    }
}
