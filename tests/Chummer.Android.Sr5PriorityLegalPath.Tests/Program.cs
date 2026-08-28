using Chummer.Android.Native;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

internal static class Program
{
    private static void Main()
    {
        BlockedAuthorityPreservesExactEvidence();
        ReviewRequiresExactAvailableCompleteAuthority();
        MissingAuthorityFailsClosed();
        Console.WriteLine("SR5 Priority legal-path projection tests passed: 3");
    }

    private static void BlockedAuthorityPreservesExactEvidence()
    {
        CharacterCreationFinalizationState state = State(
        [
            Step(CharacterCreationWizardStepIds.Attributes, complete: true, [], ["sr5-core:attributes"]),
            Step("gear", complete: false,
                [CharacterCreationFinalizationBlockers.GearDraftRequired],
                ["sr5-core:gear"])
        ],
        canReview: false,
        [CharacterCreationFinalizationBlockers.GearDraftRequired]);
        CreationPriorityLegalPathProjection projection = CreationPriorityLegalPathProjection.From(
            new CharacterCreationFinalizationResult<CharacterCreationFinalizationState>(
                CharacterCreationFinalizationOutcomes.Available,
                state,
                []));

        Require(!projection.CanOpenReview, "Incomplete required Gear enabled final review.");
        Require(projection.ContentRevision == 17 && projection.SavedRevision == 16,
            "Finalization revision binding was lost.");
        CreationPriorityLegalPathStep gear = projection.Steps.Single(step => step.StepId == "gear");
        Require(
            gear.Blockers.SequenceEqual([CharacterCreationFinalizationBlockers.GearDraftRequired])
            && gear.SourceAnchorIds.SequenceEqual(["sr5-core:gear"]),
            "Core blockers or anchors changed during projection.");
        Require(
            projection.Blockers.SequenceEqual([CharacterCreationFinalizationBlockers.GearDraftRequired]),
            "Core blocker set changed during projection.");
    }

    private static void ReviewRequiresExactAvailableCompleteAuthority()
    {
        CharacterCreationFinalizationState state = State(
        [
            Step(CharacterCreationWizardStepIds.Method, complete: true, [], ["sr5-core:priority"]),
            new CharacterCreationFinalizationStep(
                CharacterCreationWizardStepIds.MagicResonance,
                IsRequired: false,
                IsComplete: true,
                DraftDigest: null,
                Blockers: [],
                SourceAnchorIds: [])
        ],
        canReview: true,
        []);
        var available = new CharacterCreationFinalizationResult<CharacterCreationFinalizationState>(
            CharacterCreationFinalizationOutcomes.Available,
            state,
            []);

        Require(CreationPriorityLegalPathProjection.From(available).CanOpenReview,
            "Exact available complete authority did not enable review.");
        Require(!CreationPriorityLegalPathProjection.From(available with
        {
            Outcome = CharacterCreationFinalizationOutcomes.Conflict
        }).CanOpenReview, "Conflict outcome enabled review.");
        Require(!CreationPriorityLegalPathProjection.From(available with
        {
            Value = state with { CanReview = false }
        }).CanOpenReview, "Core CanReview=false was overridden.");
    }

    private static void MissingAuthorityFailsClosed()
    {
        CreationPriorityLegalPathProjection missing = CreationPriorityLegalPathProjection.From(
            new CharacterCreationFinalizationResult<CharacterCreationFinalizationState>(
                CharacterCreationFinalizationOutcomes.Unavailable,
                null,
                []));
        Require(
            !missing.CanOpenReview
            && missing.Blockers.SequenceEqual([CharacterCreationFinalizationOutcomes.Unavailable]),
            "Missing finalization authority did not fail closed.");
        Require(!CreationPriorityLegalPathProjection.From(null).CanOpenReview,
            "Loading authority enabled review.");
    }

    private static CharacterCreationFinalizationState State(
        IReadOnlyList<CharacterCreationFinalizationStep> steps,
        bool canReview,
        IReadOnlyList<string> blockers)
    {
        var binding = new CharacterCreationFinalizationBinding(
            new CharacterWorkspaceId("priority-legal-path"),
            ContentRevision: 17,
            SavedRevision: 16,
            RawCharacterXmlDigest: Digest('1'),
            AuxiliaryStateDigest: Digest('2'),
            BuildMethod: CharacterCreationBuildMethods.Priority,
            AuthorityDigest: Digest('3'));
        return new CharacterCreationFinalizationState(
            CharacterCreationFinalizationSchemas.StateV1,
            binding,
            CharacterCreated: false,
            steps,
            blockers,
            canReview,
            LastReceipt: null,
            SnapshotDigest: Digest('4'));
    }

    private static CharacterCreationFinalizationStep Step(
        string stepId,
        bool complete,
        IReadOnlyList<string> blockers,
        IReadOnlyList<string> anchors)
        => new(
            stepId,
            IsRequired: true,
            IsComplete: complete,
            DraftDigest: complete ? Digest('5') : null,
            blockers,
            anchors);

    private static string Digest(char value) => "sha256:" + new string(value, 64);

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
