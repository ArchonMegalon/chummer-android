using System.Security.Cryptography;
using System.Text;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

/// <summary>
/// Build-time authority carried by every durable skill-group action. The
/// content digest is the verified packaged Chummer data/lang generation; the
/// runtime digest binds that generation to the exact Core and Presentation
/// commits consumed by this Android product line.
/// </summary>
public sealed record Sr5CareerSkillGroupRuntimeAuthority(
    string ContractName,
    string CoreRevision,
    string PresentationRevision,
    string ContentDigest,
    string RuntimeDigest)
{
    public const string CurrentContractName =
        "chummer.android.sr5-career-skill-group-runtime/v1";
    public const string CurrentCoreRevision =
        "b1d6abd5ea0e00c5063bc6561a87c50ec1b7eb85";
    public const string CurrentPresentationRevision =
        "671289bb75994a686308cd3f3a1a52e5590f36a4";
    public const string CurrentContentDigest =
        "75f39aa795619d1d45341ebe12667fcc0b44bf77fbc7e6c534b0fe0cb86d917a";
    public const string CurrentRuntimeDigest =
        "24cdb751cd4e53afada3c9a70be5595e9231b149677d2f3002e8c7f9fe5e60df";

    public static Sr5CareerSkillGroupRuntimeAuthority Embedded { get; } = new(
        CurrentContractName,
        CurrentCoreRevision,
        CurrentPresentationRevision,
        CurrentContentDigest,
        CurrentRuntimeDigest);

    public bool IsCurrent()
        => this == Embedded
            && string.Equals(
                RuntimeDigest,
                ComputeRuntimeDigest(
                    ContractName,
                    CoreRevision,
                    PresentationRevision,
                    ContentDigest),
                StringComparison.Ordinal);

    internal static string ComputeRuntimeDigest(
        string contractName,
        string coreRevision,
        string presentationRevision,
        string contentDigest)
    {
        string payload = string.Join(
            "\n",
            contractName,
            coreRevision,
            presentationRevision,
            contentDigest,
            string.Empty);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))
            .ToLowerInvariant();
    }
}

/// <summary>
/// A reviewed SR5 skill-group advancement. The draft retains the exact typed
/// Core quote and plan; Android never rebuilds costs or legality itself.
/// </summary>
public sealed record Sr5CareerSkillGroupDraft(
    Guid OwnerId,
    CharacterWorkspaceId WorkspaceId,
    long ExpectedContentRevision,
    Sr5CareerSkillGroupRuntimeAuthority RuntimeAuthority,
    CharacterCareerSkillGroupAdvanceQuote Quote,
    CharacterCareerSkillGroupAdvancePlan Plan)
{
    public Sr5CareerActionPlan ActionPlan
        => Sr5CareerActionPlan.FromSkillGroup(
            OwnerId,
            WorkspaceId,
            ExpectedContentRevision,
            Quote,
            Plan,
            RuntimeAuthority.ContentDigest,
            RuntimeAuthority.RuntimeDigest);

    public CareerSkillGroupAdvanceRequest ToRequest()
        => new(
            WorkspaceId,
            ExpectedContentRevision,
            CharacterCareerSkillGroupAdvanceRules.RulesetId,
            Quote,
            Quote.LogicalRevision,
            Quote.SourceRevision,
            Quote.RuleDigest,
            true,
            Plan.TransactionId,
            Plan.ExpenseDateLocal);

    public bool Matches(CharacterWorkspaceId? workspaceId, long contentRevision)
        => workspaceId == WorkspaceId && contentRevision == ExpectedContentRevision;

    public bool IsExact()
    {
        if (OwnerId == Guid.Empty
            || string.IsNullOrWhiteSpace(WorkspaceId.Value)
            || ExpectedContentRevision <= 0
            || RuntimeAuthority is null
            || !RuntimeAuthority.IsCurrent()
            || !CharacterCareerSkillGroupAdvanceRules.IsCoherent(Quote)
            || !Quote.CanAdvance
            || !CharacterCareerSkillGroupAdvanceRules.IsCoherent(Plan)
            || Quote.Identity != Plan.Identity
            || !CharacterCareerSkillGroupAdvanceRules.TryPlanAdvance(
                Quote,
                Quote.LogicalRevision,
                Quote.SourceRevision,
                Quote.RuleDigest,
                confirmed: true,
                transactionIdAlreadyExists: false,
                Plan.TransactionId,
                Plan.ExpenseDateLocal,
                out CharacterCareerSkillGroupAdvancePlan expected)
            || expected != Plan)
        {
            return false;
        }

        Sr5CareerActionPlan action = ActionPlan;
        return action.OwnerId == OwnerId
            && action.ActionId == Plan.TransactionId
            && action.Kind == Sr5CareerActionKind.SkillGroupAdvance
            && action.WorkspaceId == WorkspaceId
            && action.ExpectedContentRevision == ExpectedContentRevision
            && string.Equals(
                action.RouteId,
                Sr5CareerWizardRoutes.SkillGroupReview,
                StringComparison.Ordinal)
            && string.Equals(
                action.DomainIdentity,
                Quote.Identity.InternalId.ToString("D"),
                StringComparison.Ordinal)
            && action.CostQuote.IsExact
            && action.CostQuote.KarmaCost == Quote.KarmaCost
            && action.CostQuote.NuyenCost == 0m
            && action.CostQuote.EssenceCost == 0m
            && action.CostQuote.Availability is null
            && action.CostQuote.ElapsedTime == Quote.ApplicationDuration
            && string.Equals(action.CostQuote.RuleDigest, Quote.RuleDigest, StringComparison.Ordinal)
            && string.Equals(action.CostQuote.LogicalRevision, Quote.LogicalRevision, StringComparison.Ordinal)
            && string.IsNullOrEmpty(action.CostQuote.Blocker);
    }

    public static bool TryCreate(
        CareerSkillGroupAdvanceEditorState? editor,
        CharacterCareerSkillGroupAdvanceQuote? selected,
        Guid ownerId,
        Guid expenseId,
        DateTime expenseDateLocal,
        out Sr5CareerSkillGroupDraft draft,
        out string blocker)
    {
        draft = null!;
        blocker = string.Empty;
        if (editor is null
            || string.IsNullOrWhiteSpace(editor.WorkspaceId.Value)
            || editor.ContentRevision <= 0
            || !string.Equals(
                editor.RulesetId,
                CharacterCareerSkillGroupAdvanceRules.RulesetId,
                StringComparison.Ordinal))
        {
            blocker = "The runner identity or revision is unavailable. Reopen skill-group advancement.";
            return false;
        }
        if (ownerId == Guid.Empty)
        {
            blocker = "The local Career owner identity is unavailable. Reopen skill-group advancement.";
            return false;
        }
        if (selected is null)
        {
            blocker = "Choose an exact saved skill group.";
            return false;
        }

        CharacterCareerSkillGroupAdvanceQuote[] matches = editor.SkillGroups
            .Where(candidate => candidate.Identity == selected.Identity
                && string.Equals(candidate.LogicalRevision, selected.LogicalRevision, StringComparison.Ordinal)
                && string.Equals(candidate.SourceRevision, selected.SourceRevision, StringComparison.Ordinal)
                && string.Equals(candidate.RuleDigest, selected.RuleDigest, StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        if (matches.Length != 1
            || !CharacterCareerSkillGroupAdvanceRules.IsCoherent(matches[0]))
        {
            blocker = "The selected skill-group quote is foreign, ambiguous, stale, or incoherent.";
            return false;
        }

        CharacterCareerSkillGroupAdvanceQuote authoritative = matches[0];
        if (!authoritative.CanAdvance)
        {
            blocker = BlockerText(authoritative.Blocker);
            return false;
        }

        DateTime serializedExpenseDate = DateTime.SpecifyKind(
            new DateTime(
                expenseDateLocal.Year,
                expenseDateLocal.Month,
                expenseDateLocal.Day,
                expenseDateLocal.Hour,
                expenseDateLocal.Minute,
                expenseDateLocal.Second),
            DateTimeKind.Unspecified);
        if (!CharacterCareerSkillGroupAdvanceRules.TryPlanAdvance(
                authoritative,
                authoritative.LogicalRevision,
                authoritative.SourceRevision,
                authoritative.RuleDigest,
                confirmed: true,
                transactionIdAlreadyExists: false,
                expenseId,
                serializedExpenseDate,
                out CharacterCareerSkillGroupAdvancePlan plan))
        {
            blocker = "Core rejected the confirmed expense identity, date, quote, or rule revisions.";
            return false;
        }

        Sr5CareerSkillGroupDraft candidate = new(
            ownerId,
            editor.WorkspaceId,
            editor.ContentRevision,
            Sr5CareerSkillGroupRuntimeAuthority.Embedded,
            authoritative,
            plan);
        if (!candidate.IsExact())
        {
            blocker = "The exact SR5 CostQuote and CareerActionPlan could not be bound.";
            return false;
        }

        draft = candidate;
        return true;
    }

    public static string BlockerText(CharacterCareerSkillGroupAdvanceBlocker blocker)
        => blocker switch
        {
            CharacterCareerSkillGroupAdvanceBlocker.NotCareerCharacter =>
                "Skill-group advancement is available only after character creation.",
            CharacterCareerSkillGroupAdvanceBlocker.UnsupportedRuleset =>
                "This quote is not governed by the SR5 skill-group authority.",
            CharacterCareerSkillGroupAdvanceBlocker.ForeignTarget =>
                "The selected group is not an exact saved SR5 skill-group identity.",
            CharacterCareerSkillGroupAdvanceBlocker.InvalidMemberProjection =>
                "The exact enabled member projection is unavailable.",
            CharacterCareerSkillGroupAdvanceBlocker.Broken =>
                "This skill group is broken and cannot advance as a group.",
            CharacterCareerSkillGroupAdvanceBlocker.Disabled =>
                "This skill group is disabled by the active rules and improvements.",
            CharacterCareerSkillGroupAdvanceBlocker.AtMaximum =>
                "This skill group is already at the active maximum rating.",
            CharacterCareerSkillGroupAdvanceBlocker.InsufficientKarma =>
                "The runner does not have enough Karma for this advancement.",
            CharacterCareerSkillGroupAdvanceBlocker.None => string.Empty,
            _ => "Core blocked this skill-group advancement."
        };
}

public sealed record Sr5CareerSkillGroupCheckpoint(
    int SchemaVersion,
    long Version,
    string RouteId,
    Sr5CareerCheckpointPhase Phase,
    Sr5CareerSkillGroupDraft Draft,
    string IdempotencyKey)
{
    public const int CurrentSchemaVersion = 2;

    public static Sr5CareerSkillGroupCheckpoint FromDraft(
        Sr5CareerSkillGroupDraft draft,
        Sr5CareerCheckpointPhase phase = Sr5CareerCheckpointPhase.Reviewed)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return new(
            CurrentSchemaVersion,
            Version: 1,
            Sr5CareerWizardRoutes.SkillGroupReview,
            phase,
            draft,
            draft.ActionPlan.IdempotencyKey);
    }

    public bool IsStructurallyValid()
        => SchemaVersion == CurrentSchemaVersion
            && Version > 0
            && string.Equals(RouteId, Sr5CareerWizardRoutes.SkillGroupReview, StringComparison.Ordinal)
            && Enum.IsDefined(Phase)
            && Draft is not null
            && Draft.IsExact()
            && IdempotencyKey is { Length: 64 }
            && IdempotencyKey.All(static character =>
                character is >= '0' and <= '9' or >= 'a' and <= 'f')
            && string.Equals(
                IdempotencyKey,
                Draft.ActionPlan.IdempotencyKey,
                StringComparison.Ordinal);

    public bool TryResume(
        CareerSkillGroupAdvanceEditorState? editor,
        out Sr5CareerSkillGroupDraft draft,
        out string blocker)
    {
        draft = null!;
        if (!IsStructurallyValid()
            || Phase != Sr5CareerCheckpointPhase.Reviewed)
        {
            blocker = "The saved skill-group action is applying, applied, or uses an unsupported schema.";
            return false;
        }
        if (editor is null
            || editor.WorkspaceId != Draft.WorkspaceId
            || editor.ContentRevision != Draft.ExpectedContentRevision)
        {
            blocker = "The saved skill-group review belongs to another runner revision.";
            return false;
        }
        if (!Sr5CareerSkillGroupDraft.TryCreate(
                editor,
                Draft.Quote,
                Draft.OwnerId,
                Draft.Plan.TransactionId,
                Draft.Plan.ExpenseDateLocal,
                out Sr5CareerSkillGroupDraft resumed,
                out blocker)
            || !MatchesActionDraft(resumed))
        {
            draft = null!;
            blocker = string.IsNullOrWhiteSpace(blocker)
                ? "The saved skill-group review no longer matches its exact quote and plan."
                : blocker;
            return false;
        }

        draft = resumed;
        return true;
    }

    public bool MatchesActionDraft(Sr5CareerSkillGroupDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return IsStructurallyValid()
            && Draft.OwnerId == draft.OwnerId
            && Draft.WorkspaceId == draft.WorkspaceId
            && Draft.ExpectedContentRevision == draft.ExpectedContentRevision
            && Draft.RuntimeAuthority == draft.RuntimeAuthority
            && Draft.Quote.Identity == draft.Quote.Identity
            && string.Equals(Draft.Quote.LogicalRevision, draft.Quote.LogicalRevision, StringComparison.Ordinal)
            && string.Equals(Draft.Quote.SourceRevision, draft.Quote.SourceRevision, StringComparison.Ordinal)
            && string.Equals(Draft.Quote.RuleDigest, draft.Quote.RuleDigest, StringComparison.Ordinal)
            && Draft.Plan == draft.Plan
            && string.Equals(IdempotencyKey, draft.ActionPlan.IdempotencyKey, StringComparison.Ordinal);
    }
}

public enum Sr5CareerSkillGroupRecoveryStatus
{
    AppliedVerified,
    NotAppliedVerified,
    OutcomeUnknown
}

public sealed record Sr5CareerSkillGroupRecoveryResolution(
    Sr5CareerSkillGroupRecoveryStatus Status,
    string WorkspaceId,
    Guid OwnerId,
    Guid ActionId,
    long CheckpointVersion,
    CharacterCareerSkillGroupAdvanceReceipt? Receipt,
    string Message,
    string AuthorityProof);

public enum Sr5CareerSkillGroupApplyStatus
{
    Applied,
    RejectedBeforeMutation,
    OutcomeUnknown
}

public sealed record Sr5CareerSkillGroupApplyResult(
    Sr5CareerSkillGroupApplyStatus Status,
    Sr5CareerActionPlan ActionPlan,
    long? SavedContentRevision,
    CharacterCareerSkillGroupAdvanceReceipt? Receipt,
    Sr5CareerSkillGroupRecoveryResolution Resolution,
    string Message);
