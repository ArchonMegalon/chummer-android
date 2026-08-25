using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

/// <summary>
/// Exact binary/content authority carried by every phone quality transaction.
/// The Core/Presentation commits are immutable inputs, while the content and
/// runtime digests prevent a persisted review from crossing an app generation.
/// </summary>
public sealed record Sr5CareerQualityRuntimeAuthority(
    string ContractName,
    string CoreRevision,
    string PresentationRevision,
    string ContentDigest,
    string RuntimeDigest)
{
    public const string CurrentContractName =
        "chummer.android.sr5-career-quality-runtime/v1";
    public const string CurrentCoreRevision =
        "3a0ac44854004dff0c08807d839cd1fdae1c9a65";
    public const string CurrentPresentationRevision =
        "ac4ebc482c632efa2e6ecadf1df884963fc56d28";
    public const string CurrentContentDigest =
        "61dddaad0bcbd80f3e8a17bfc7b875787dffb6a854fb1672b847b766dd05c0ff";
    public const string CurrentRuntimeDigest =
        "9596f74f290961f90bbd359943d72e0018826c074afcb9c2be29d9c29fd93551";

    public static Sr5CareerQualityRuntimeAuthority Embedded { get; } = new(
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
/// A reviewed quality operation. Android retains the complete Presentation
/// review rather than a label or a reconstructed rule request.
/// </summary>
public sealed record Sr5CareerQualityDraft(
    Guid OwnerId,
    CareerQualityReview Review,
    Guid TransactionId,
    DateTime ExpenseDateLocal,
    Sr5CareerQualityRuntimeAuthority RuntimeAuthority)
{
    public CharacterWorkspaceId WorkspaceId => Review.Draft.WorkspaceId;
    public long ExpectedWorkspaceRevision => Review.Draft.ExpectedWorkspaceRevision;
    public long ExpectedSavedRevision => Review.Draft.ExpectedSavedRevision;

    public Sr5CareerActionPlan ActionPlan
        => Sr5CareerActionPlan.FromQuality(
            OwnerId,
            Review,
            TransactionId,
            ExpenseDateLocal,
            RuntimeAuthority.ContentDigest,
            RuntimeAuthority.RuntimeDigest);

    public bool Matches(
        CharacterWorkspaceId? workspaceId,
        long workspaceRevision,
        long savedRevision)
        => workspaceId == WorkspaceId
            && workspaceRevision == ExpectedWorkspaceRevision
            && savedRevision == ExpectedSavedRevision;

    public bool IsExact()
    {
        CharacterCareerQualityQuote? quote = Review?.Quote;
        CareerQualityDraft? draft = Review?.Draft;
        if (OwnerId == Guid.Empty
            || draft is null
            || quote is null
            || !Guid.TryParseExact(draft.ExpectedOwnerId, "D", out Guid authorityOwner)
            || authorityOwner != OwnerId
            || string.IsNullOrWhiteSpace(draft.WorkspaceId.Value)
            || draft.ExpectedWorkspaceRevision <= 0
            || draft.ExpectedSavedRevision != draft.ExpectedWorkspaceRevision
            || !string.Equals(draft.ExpectedRulesetId, CharacterCareerQualityRules.RulesetId, StringComparison.Ordinal)
            || draft.Operation != quote.Operation
            || draft.Identity != quote.Identity
            || !string.Equals(draft.ExpectedLogicalRevision, quote.LogicalRevision, StringComparison.Ordinal)
            || !string.Equals(draft.ExpectedSourceRevision, quote.SourceRevision, StringComparison.Ordinal)
            || !string.Equals(draft.ExpectedRuleDigest, quote.RuleDigest, StringComparison.Ordinal)
            || !string.Equals(draft.ExpectedRuntimeFingerprint, quote.Binding.RuntimeFingerprint, StringComparison.Ordinal)
            || !string.Equals(draft.ExpectedContentDigest, quote.Binding.ContentDigest, StringComparison.Ordinal)
            || quote.Binding.WorkspaceRevision != draft.ExpectedWorkspaceRevision
            || quote.Binding.SavedRevision != draft.ExpectedSavedRevision
            || !string.Equals(quote.Binding.OwnerId, draft.ExpectedOwnerId, StringComparison.Ordinal)
            || !string.Equals(quote.Binding.WorkspaceId, draft.WorkspaceId.Value, StringComparison.Ordinal)
            || RuntimeAuthority is null
            || !RuntimeAuthority.IsCurrent()
            || !string.Equals(quote.Binding.RuntimeFingerprint, RuntimeAuthority.RuntimeDigest, StringComparison.Ordinal)
            || !string.Equals(quote.Binding.ContentDigest, RuntimeAuthority.ContentDigest, StringComparison.Ordinal)
            || TransactionId == Guid.Empty
            || DateTime.SpecifyKind(ExpenseDateLocal, DateTimeKind.Unspecified)
                < CharacterCareerQualityRules.MinimumExpenseDate
            || DateTime.SpecifyKind(ExpenseDateLocal, DateTimeKind.Unspecified)
                > CharacterCareerQualityRules.MaximumExpenseDate
            || !CharacterCareerQualityRules.IsCoherent(quote)
            || !quote.CanApply
            || quote.Blocker != CharacterCareerQualityBlocker.None
            || !quote.Definition.Implemented
            || !quote.Definition.SourceEnabled
            || !quote.Authority.GmAllows
            || !quote.Authority.DefinitionProjectionIsExact
            || !quote.Authority.IdentityProjectionIsExact
            || !quote.Authority.Eligibility.IsExact
            || !quote.Authority.Effects.IsExact
            || quote.Authority.Effects.UnsupportedFamilies.Count != 0)
        {
            return false;
        }

        Sr5CareerActionPlan action = ActionPlan;
        return action.OwnerId == OwnerId
            && action.ActionId == TransactionId
            && action.Kind == Sr5CareerActionKind.QualityTransaction
            && action.WorkspaceId == WorkspaceId
            && action.ExpectedContentRevision == ExpectedWorkspaceRevision
            && string.Equals(action.RouteId, Sr5CareerWizardRoutes.QualityReview, StringComparison.Ordinal)
            && string.Equals(
                action.DomainIdentity,
                $"{quote.Operation}:{quote.Identity.InternalId:D}:{quote.Identity.SourceId:D}",
                StringComparison.Ordinal)
            && action.CostQuote.IsExact
            && action.CostQuote.KarmaCost == quote.RuleKarmaCost
            && action.CostQuote.ElapsedTime == quote.ApplicationDuration
            && string.Equals(action.CostQuote.RuleDigest, quote.RuleDigest, StringComparison.Ordinal)
            && string.Equals(action.CostQuote.LogicalRevision, quote.LogicalRevision, StringComparison.Ordinal)
            && string.IsNullOrEmpty(action.CostQuote.Blocker);
    }

    public static bool TryCreate(
        CareerQualityEditorState? editor,
        CareerQualityReview? review,
        Guid ownerId,
        Guid transactionId,
        DateTime expenseDateLocal,
        out Sr5CareerQualityDraft draft,
        out string blocker)
    {
        draft = null!;
        blocker = string.Empty;
        if (editor is null
            || string.IsNullOrWhiteSpace(editor.WorkspaceId.Value)
            || editor.WorkspaceRevision <= 0
            || editor.SavedRevision != editor.WorkspaceRevision)
        {
            blocker = "The exact clean saved runner revision is unavailable.";
            return false;
        }
        if (editor.OmittedCandidateCount != 0 || editor.OmittedReceiptCount != 0)
        {
            blocker = "Quality authority is ambiguous or recovery evidence was omitted. Resolve it before continuing.";
            return false;
        }
        if (review is null || review.Draft is null || review.Quote is null)
        {
            blocker = "The exact refreshed quality review is unavailable.";
            return false;
        }

        CharacterCareerQualityQuote[] matches = editor.Quotes
            .Where(candidate => candidate.Operation == review.Quote.Operation
                && candidate.Identity == review.Quote.Identity
                && string.Equals(candidate.LogicalRevision, review.Quote.LogicalRevision, StringComparison.Ordinal)
                && string.Equals(candidate.SourceRevision, review.Quote.SourceRevision, StringComparison.Ordinal)
                && string.Equals(candidate.RuleDigest, review.Quote.RuleDigest, StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        if (matches.Length != 1
            || !CharacterCareerQualityRules.IsCoherent(matches[0]))
        {
            blocker = "The selected quality is foreign, ambiguous, stale, or incoherent.";
            return false;
        }

        Sr5CareerQualityDraft candidate = new(
            ownerId,
            review,
            transactionId,
            NormalizeExpenseDate(expenseDateLocal),
            Sr5CareerQualityRuntimeAuthority.Embedded);
        if (!candidate.IsExact())
        {
            blocker = BlockerText(review.Quote.Blocker);
            if (string.IsNullOrWhiteSpace(blocker))
            {
                blocker = "The quality source, effect, GM, runtime, content, owner, or revision authority is not exact.";
            }
            return false;
        }
        draft = candidate;
        return true;
    }

    public static string BlockerText(CharacterCareerQualityBlocker blocker)
        => blocker switch
        {
            CharacterCareerQualityBlocker.None => string.Empty,
            CharacterCareerQualityBlocker.NotCareerCharacter => "Quality changes are available only in Career mode.",
            CharacterCareerQualityBlocker.UnsupportedRuleset => "This quality is not governed by the exact SR5 authority.",
            CharacterCareerQualityBlocker.InvalidDefinitionProjection => "The exact enabled source definition is unavailable.",
            CharacterCareerQualityBlocker.InvalidIdentityProjection => "The quality InternalId/SourceId projection is ambiguous.",
            CharacterCareerQualityBlocker.ForeignOrCollidingTarget => "The proposed or existing quality identity is foreign or colliding.",
            CharacterCareerQualityBlocker.SourceDisabled => "The source book or custom-data package is disabled.",
            CharacterCareerQualityBlocker.UnimplementedDefinition => "This quality is not implemented by the exact SR5 effect authority.",
            CharacterCareerQualityBlocker.CareerUnavailable => "This quality cannot be changed in Career mode.",
            CharacterCareerQualityBlocker.GmRestricted => "The active GM policy does not permit this quality operation.",
            CharacterCareerQualityBlocker.InvalidEligibilityProjection => "Eligibility could not be projected exactly.",
            CharacterCareerQualityBlocker.MissingRequirement => "A required quality, metatype, or other prerequisite is missing.",
            CharacterCareerQualityBlocker.ForbiddenConflict => "An active quality conflict forbids this operation.",
            CharacterCareerQualityBlocker.DuplicateOrLevelLimit => "The quality is duplicated or already at its exact level limit.",
            CharacterCareerQualityBlocker.UnremovableOrigin => "This quality origin cannot be removed in Career mode.",
            CharacterCareerQualityBlocker.InvalidCostDiscountProjection => "The quality discount cannot be calculated exactly.",
            CharacterCareerQualityBlocker.InvalidEffectProjection => "The complete quality effect delta is unavailable.",
            CharacterCareerQualityBlocker.UnsupportedEffectFamily => "At least one quality effect family is unsupported; no partial apply is allowed.",
            CharacterCareerQualityBlocker.InsufficientKarma => "The runner does not have enough Karma.",
            _ => "Core blocked this quality operation."
        };

    internal static DateTime NormalizeExpenseDate(DateTime value)
        => DateTime.SpecifyKind(
            new DateTime(
                value.Year,
                value.Month,
                value.Day,
                value.Hour,
                value.Minute,
                value.Second),
            DateTimeKind.Unspecified);
}

public sealed record Sr5CareerQualityCheckpoint(
    int SchemaVersion,
    long Version,
    string RouteId,
    Sr5CareerCheckpointPhase Phase,
    Sr5CareerQualityDraft Draft,
    string IdempotencyKey)
{
    public const int CurrentSchemaVersion = 1;

    public static Sr5CareerQualityCheckpoint FromDraft(
        Sr5CareerQualityDraft draft,
        Sr5CareerCheckpointPhase phase = Sr5CareerCheckpointPhase.Reviewed)
        => new(
            CurrentSchemaVersion,
            Version: 1,
            Sr5CareerWizardRoutes.QualityReview,
            phase,
            draft,
            draft.ActionPlan.IdempotencyKey);

    public bool IsStructurallyValid()
        => SchemaVersion == CurrentSchemaVersion
            && Version > 0
            && string.Equals(RouteId, Sr5CareerWizardRoutes.QualityReview, StringComparison.Ordinal)
            && Enum.IsDefined(Phase)
            && Draft is not null
            && Draft.IsExact()
            && IdempotencyKey is { Length: 64 }
            && IdempotencyKey.All(static character =>
                character is >= '0' and <= '9' or >= 'a' and <= 'f')
            && string.Equals(IdempotencyKey, Draft.ActionPlan.IdempotencyKey, StringComparison.Ordinal);

    public bool TryResume(
        CareerQualityEditorState? editor,
        out Sr5CareerQualityDraft draft,
        out string blocker)
    {
        draft = null!;
        if (!IsStructurallyValid() || Phase != Sr5CareerCheckpointPhase.Reviewed)
        {
            blocker = "The saved quality action is applying, applied, or uses an unsupported schema.";
            return false;
        }
        if (editor is null
            || editor.WorkspaceId != Draft.WorkspaceId
            || editor.WorkspaceRevision != Draft.ExpectedWorkspaceRevision
            || editor.SavedRevision != Draft.ExpectedSavedRevision
            || editor.OmittedCandidateCount != 0
            || editor.OmittedReceiptCount != 0)
        {
            blocker = "The saved quality review belongs to another or ambiguous runner revision.";
            return false;
        }
        CharacterCareerQualityQuote[] matches = editor.Quotes
            .Where(candidate => candidate.Operation == Draft.Review.Quote.Operation
                && candidate.Identity == Draft.Review.Quote.Identity
                && string.Equals(candidate.LogicalRevision, Draft.Review.Quote.LogicalRevision, StringComparison.Ordinal)
                && string.Equals(candidate.SourceRevision, Draft.Review.Quote.SourceRevision, StringComparison.Ordinal)
                && string.Equals(candidate.RuleDigest, Draft.Review.Quote.RuleDigest, StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        if (matches.Length != 1
            || !QuotesMatch(matches[0], Draft.Review.Quote))
        {
            blocker = "The quality source, effects, GM policy, or exact rule quote changed.";
            return false;
        }
        draft = Draft;
        blocker = string.Empty;
        return true;
    }

    internal static bool QuotesMatch(
        CharacterCareerQualityQuote left,
        CharacterCareerQualityQuote right)
        => CharacterCareerQualityRules.IsCoherent(left)
            && CharacterCareerQualityRules.IsCoherent(right)
            && string.Equals(
                JsonSerializer.Serialize(left),
                JsonSerializer.Serialize(right),
                StringComparison.Ordinal);

    public bool MatchesActionDraft(Sr5CareerQualityDraft draft)
        => IsStructurallyValid()
            && draft is not null
            && draft.IsExact()
            && string.Equals(
                JsonSerializer.Serialize(Draft),
                JsonSerializer.Serialize(draft),
                StringComparison.Ordinal)
            && string.Equals(IdempotencyKey, draft.ActionPlan.IdempotencyKey, StringComparison.Ordinal);
}

public enum Sr5CareerQualityRecoveryStatus
{
    AppliedVerified,
    NotAppliedVerified,
    OutcomeUnknown
}

public sealed record Sr5CareerQualityRecoveryResolution(
    Sr5CareerQualityRecoveryStatus Status,
    string WorkspaceId,
    Guid OwnerId,
    Guid ActionId,
    long CheckpointVersion,
    CharacterCareerQualityReceipt? Receipt,
    string Message,
    string AuthorityProof);

public enum Sr5CareerQualityApplyStatus
{
    Applied,
    RejectedBeforeMutation,
    OutcomeUnknown
}

public sealed record Sr5CareerQualityApplyResult(
    Sr5CareerQualityApplyStatus Status,
    Sr5CareerActionPlan ActionPlan,
    long? SavedWorkspaceRevision,
    CharacterCareerQualityReceipt? Receipt,
    Sr5CareerQualityRecoveryResolution Resolution,
    string Message);
