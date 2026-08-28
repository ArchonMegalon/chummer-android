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
        MachineDigestNormalizationIsExactAndFailClosed();
        PersistedReceiptRequiresExactTypedAuthority();
        PersistedReceiptRejectsDriftTamperingAndOtherBuildMethods();
        Console.WriteLine("SR5 Priority legal-path projection tests passed: 6");
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

    private static void PersistedReceiptRequiresExactTypedAuthority()
    {
        CharacterCreationFinalizationReceipt receipt = Receipt();
        CharacterCreationFinalizationState state = CreatedState(receipt);
        var result = new CharacterCreationFinalizationResult<CharacterCreationFinalizationState>(
            CharacterCreationFinalizationOutcomes.Blocked,
            state,
            [CharacterCreationFinalizationBlockers.CharacterAlreadyCreated]);

        CharacterCreationFinalizationReceipt? resolved =
            CreationPriorityLegalPathProjection.ResolvePersistedPriorityReceipt(
                result,
                receipt.WorkspaceId,
                receipt.ContentRevision,
                receipt.SavedRevision);

        Require(ReferenceEquals(receipt, resolved),
            "The exact persisted typed receipt was not returned.");
        Require(resolved?.ReceiptDigest == receipt.ReceiptDigest,
            "The persisted receipt digest was re-derived or truncated at the phone boundary.");
    }

    private static void MachineDigestNormalizationIsExactAndFailClosed()
    {
        string canonical = Digest('a');
        Require(CreationPriorityLegalPathProjection.NormalizeMachineDigestPayload(canonical)
                == new string('a', 64),
            "The canonical digest payload was changed or truncated.");
        foreach (string invalid in new[]
                 {
                     string.Empty,
                     new string('a', 64),
                     "sha512:" + new string('a', 64),
                     "sha256:" + new string('A', 64),
                     "sha256:" + new string('g', 64),
                     "sha256:" + new string('a', 63)
                 })
        {
            Require(CreationPriorityLegalPathProjection.NormalizeMachineDigestPayload(invalid) is null,
                $"Invalid digest representation was normalized: {invalid}");
        }
    }

    private static void PersistedReceiptRejectsDriftTamperingAndOtherBuildMethods()
    {
        CharacterCreationFinalizationReceipt receipt = Receipt();
        CharacterCreationFinalizationState state = CreatedState(receipt);
        var result = new CharacterCreationFinalizationResult<CharacterCreationFinalizationState>(
            CharacterCreationFinalizationOutcomes.Blocked,
            state,
            [CharacterCreationFinalizationBlockers.CharacterAlreadyCreated]);

        Require(CreationPriorityLegalPathProjection.ResolvePersistedPriorityReceipt(
                    result, receipt.WorkspaceId, receipt.ContentRevision + 1, receipt.SavedRevision) is null,
            "A stale overview revision exposed a persisted receipt authority.");
        Require(CreationPriorityLegalPathProjection.ResolvePersistedPriorityReceipt(
                    result with { Value = state with { LastReceipt = receipt with { ReceiptDigest = Digest('f') } } },
                    receipt.WorkspaceId, receipt.ContentRevision, receipt.SavedRevision) is null,
            "A tampered receipt digest was exposed as authority.");
        Require(CreationPriorityLegalPathProjection.ResolvePersistedPriorityReceipt(
                    result with
                    {
                        Value = state with
                        {
                            Binding = state.Binding with { BuildMethod = CharacterCreationBuildMethods.SumToTen },
                            LastReceipt = receipt with { BuildMethod = CharacterCreationBuildMethods.SumToTen }
                        }
                    },
                    receipt.WorkspaceId, receipt.ContentRevision, receipt.SavedRevision) is null,
            "A non-Priority finalization receipt was exposed on the Priority authority surface.");
        Require(CreationPriorityLegalPathProjection.ResolvePersistedPriorityReceipt(
                    result with { Value = null }, receipt.WorkspaceId,
                    receipt.ContentRevision, receipt.SavedRevision) is null,
            "Missing persisted typed state did not fail closed.");
        Require(CreationPriorityLegalPathProjection.ResolvePersistedPriorityReceipt(
                    result with { Outcome = CharacterCreationFinalizationOutcomes.Available },
                    receipt.WorkspaceId, receipt.ContentRevision, receipt.SavedRevision) is null,
            "A non-Career finalization lifecycle outcome exposed persisted receipt authority.");
    }

    private static CharacterCreationFinalizationState CreatedState(
        CharacterCreationFinalizationReceipt receipt)
    {
        var binding = new CharacterCreationFinalizationBinding(
            receipt.WorkspaceId,
            receipt.ContentRevision,
            receipt.SavedRevision,
            receipt.RawCharacterXmlDigest,
            Digest('9'),
            receipt.BuildMethod,
            receipt.AuthorityDigest);
        var state = new CharacterCreationFinalizationState(
            CharacterCreationFinalizationSchemas.StateV1,
            binding,
            CharacterCreated: true,
            Steps: [],
            Blockers: [CharacterCreationFinalizationBlockers.CharacterAlreadyCreated],
            CanReview: false,
            LastReceipt: receipt,
            SnapshotDigest: string.Empty);
        return state with
        {
            SnapshotDigest = CharacterCreationFinalizationDigest.Compute(
                state with { SnapshotDigest = string.Empty })
        };
    }

    private static CharacterCreationFinalizationReceipt Receipt()
    {
        var receipt = new CharacterCreationFinalizationReceipt(
            CharacterCreationFinalizationSchemas.ReceiptV1,
            ReceiptId: "receipt-priority-legal-path",
            WorkspaceId: new CharacterWorkspaceId("priority-legal-path"),
            IdempotencyKeyDigest: Digest('1'),
            CommandDigest: Digest('2'),
            PreviousContentRevision: 16,
            ContentRevision: 17,
            PreviousSavedRevision: 16,
            SavedRevision: 17,
            PreviousRawCharacterXmlDigest: Digest('3'),
            RawCharacterXmlDigest: Digest('4'),
            PreviousAuxiliaryStateDigest: Digest('5'),
            AuthorityDigest: Digest('6'),
            PreviewDigest: Digest('7'),
            PlanDigest: Digest('8'),
            BuildMethod: CharacterCreationBuildMethods.Priority,
            CharacterCreated: true,
            RequiresFreshCareerReopen: true,
            PreviousReceiptDigest: CharacterCreationFinalizationDigest.ReceiptLedgerRootDigest,
            ReceiptDigest: string.Empty);
        return receipt with
        {
            ReceiptDigest = CharacterCreationFinalizationDigest.ComputeReceiptDigest(receipt)
        };
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
