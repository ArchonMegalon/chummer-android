using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Android.Native;

public sealed record Sr5AfterRunWorkspaceSnapshot(
    CharacterWorkspaceId WorkspaceId,
    long ContentRevision,
    long SavedRevision,
    string RulesetId,
    bool Created,
    int CurrentStreetCred,
    int CurrentNotoriety,
    int CurrentPublicAwareness,
    int CurrentKarma,
    string CharacterProjectionDigest)
{
    public bool IsExact()
        => CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(WorkspaceId)
            && ContentRevision > 0
            && SavedRevision == ContentRevision
            && string.Equals(
                RulesetId,
                CharacterAfterRunSettlementRules.RulesetId,
                StringComparison.Ordinal)
            && Created
            && CurrentStreetCred is >= 0 and <= CharacterAfterRunSettlementRules.MaximumValue
            && CurrentNotoriety is >= 0 and <= CharacterAfterRunSettlementRules.MaximumValue
            && CurrentPublicAwareness is >= 0 and <= CharacterAfterRunSettlementRules.MaximumValue
            && CurrentKarma is >= 0 and <= CharacterAfterRunSettlementRules.MaximumValue
            && CharacterAfterRunSettlementRules.IsCanonicalDigest(
                CharacterProjectionDigest);
}

public interface IAndroidAfterRunWorkspaceSnapshotSource
{
    bool TryRead(
        CharacterWorkspaceId workspaceId,
        out Sr5AfterRunWorkspaceSnapshot snapshot,
        out string blocker);
}

public sealed record Sr5AfterRunManualProposalSubmission(
    CharacterWorkspaceId WorkspaceId,
    long ExpectedWorkspaceRevision,
    CharacterAfterRunSettlementIdentity Identity,
    string RunTitle,
    DateTimeOffset CompletedAt,
    int KarmaAward,
    decimal NuyenAward,
    string RewardReceiptDigest,
    bool TargetOwnedByCharacter,
    bool RunCompleted,
    int CurrentHeat,
    int HeatDelta,
    int StreetCredDelta,
    int NotorietyDelta,
    int PublicAwarenessDelta,
    CharacterAfterRunSettlementSettings Settings,
    IReadOnlyList<CharacterAfterRunContactProposal> ContactProposals,
    string ExpectedGmActorId,
    Guid GmReviewId,
    string GmReviewReason,
    bool GmApproved,
    string ExpectedOwnerActorId,
    Guid OwnerReviewId,
    string OwnerReviewReason,
    bool OwnerApproved);

public sealed record Sr5AfterRunManualProposal(
    int SchemaVersion,
    CharacterWorkspaceId WorkspaceId,
    long WorkspaceRevision,
    string CharacterProjectionDigest,
    Sr5AfterRunRewardContext RewardContext,
    CharacterAfterRunSettlementProposalProjection Projection,
    string ProposalDigest)
{
    public const int CurrentSchemaVersion = 1;

    public bool IsExact()
        => Sr5AfterRunManualProposalIntegrity.IsExact(this);
}

public sealed record Sr5AfterRunManualProposalLedger(
    int SchemaVersion,
    long Version,
    IReadOnlyList<Sr5AfterRunManualProposal> Proposals,
    string LedgerDigest)
{
    public const int CurrentSchemaVersion = 1;

    public bool IsExact()
        => Sr5AfterRunManualProposalIntegrity.IsExact(this);
}

public sealed record Sr5AfterRunManualProposalPublishResult(
    bool Published,
    bool Replayed,
    Sr5AfterRunManualProposal? Proposal,
    string Blocker);

public interface ISr5AfterRunManualProposalAuthority
{
    Sr5AfterRunManualProposalPublishResult Publish(
        Sr5AfterRunManualProposalSubmission submission);
}

internal interface ISr5AfterRunManualProposalBackend
{
    string Read();
    void Write(string payload);
}

internal sealed class FileSr5AfterRunManualProposalBackend :
    ISr5AfterRunManualProposalBackend
{
    private readonly string _path;

    public FileSr5AfterRunManualProposalBackend(string statePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statePath);
        string root = Path.GetFullPath(statePath);
        Directory.CreateDirectory(root);
        _path = Path.Combine(root, "sr5-after-run-manual-proposals.v1.json");
    }

    public string Read()
        => File.Exists(_path)
            ? File.ReadAllText(_path, Encoding.UTF8)
            : string.Empty;

    public void Write(string payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);
        string directory = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException(
                "The After Run proposal path has no parent directory.");
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(
            directory,
            $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.next");
        try
        {
            using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough))
            using (var writer = new StreamWriter(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(payload);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}

/// <summary>
/// Android-owned manual run-result authority. It publishes only a complete,
/// Core-preflighted proposal bound to the exact clean saved workspace payload.
/// The same durable record backs run discovery and Core's bounded proposal seam.
/// Empty, stale, malformed or tampered state remains explicitly unavailable.
/// </summary>
public sealed class Sr5AfterRunManualProposalSource :
    IAndroidAfterRunProposalCatalog,
    ICharacterAfterRunSettlementProposalProjectionSource,
    ISr5AfterRunManualProposalAuthority
{
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false
    };

    private readonly IAndroidAfterRunWorkspaceSnapshotSource _snapshots;
    private readonly ISr5AfterRunManualProposalBackend _backend;

    internal Sr5AfterRunManualProposalSource(
        IAndroidAfterRunWorkspaceSnapshotSource snapshots,
        ISr5AfterRunManualProposalBackend backend)
    {
        _snapshots = snapshots ?? throw new ArgumentNullException(nameof(snapshots));
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    public Sr5AfterRunProposalCatalogResult Load(CharacterWorkspaceId workspaceId)
    {
        lock (Gate)
        {
            if (!TryReadLedgerLocked(
                    out Sr5AfterRunManualProposalLedger? ledger,
                    out string blocker))
            {
                return new(
                    string.IsNullOrWhiteSpace(blocker)
                        ? Sr5AfterRunCatalogStatus.Missing
                        : Sr5AfterRunCatalogStatus.Corrupt,
                    [],
                    0,
                    [string.IsNullOrWhiteSpace(blocker)
                        ? "No fully validated manual After Run proposal is registered for this runner."
                        : blocker]);
            }
            if (!_snapshots.TryRead(workspaceId, out Sr5AfterRunWorkspaceSnapshot snapshot,
                    out blocker)
                || !snapshot.IsExact())
            {
                return new(
                    Sr5AfterRunCatalogStatus.Unavailable,
                    [],
                    0,
                    [string.IsNullOrWhiteSpace(blocker)
                        ? "The exact clean saved runner projection is unavailable."
                        : blocker]);
            }

            Sr5AfterRunManualProposal[] candidates = ledger!.Proposals
                .Where(proposal => MatchesSnapshot(proposal, snapshot))
                .OrderBy(static proposal => proposal.Projection.Identity.ProposalId)
                .ToArray();
            if (candidates.Length == 0)
            {
                return new(
                    Sr5AfterRunCatalogStatus.Missing,
                    [],
                    0,
                    ["No fully validated manual After Run proposal is bound to this saved runner revision. Enter or reopen an exact run result."]);
            }
            return new(
                Sr5AfterRunCatalogStatus.Available,
                candidates.Select(static proposal => new Sr5AfterRunProposalCatalogEntry(
                    proposal.Projection.Identity,
                    proposal.RewardContext)).ToArray(),
                0,
                []);
        }
    }

    public CharacterAfterRunSettlementProposalProjectionResult Read(
        CharacterAfterRunSettlementProposalProjectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(
                request.WorkspaceId)
            || request.WorkspaceRevision <= 0
            || request.Identity is null
            || request.Identity.ProposalId == Guid.Empty
            || request.Identity.RunId == Guid.Empty
            || request.Identity.CharacterId == Guid.Empty
            || !CharacterAfterRunSettlementRules.IsCanonicalDigest(
                request.CharacterProjectionDigest))
        {
            return new(
                CharacterAfterRunSettlementProposalProjectionOutcome.Corrupt,
                request.WorkspaceId,
                request.WorkspaceRevision,
                request.CharacterProjectionDigest,
                Error: "invalid_android_after_run_projection_request");
        }

        lock (Gate)
        {
            if (!TryReadLedgerLocked(
                    out Sr5AfterRunManualProposalLedger? ledger,
                    out string blocker))
            {
                return new(
                    string.IsNullOrWhiteSpace(blocker)
                        ? CharacterAfterRunSettlementProposalProjectionOutcome.Unavailable
                        : CharacterAfterRunSettlementProposalProjectionOutcome.Corrupt,
                    request.WorkspaceId,
                    request.WorkspaceRevision,
                    request.CharacterProjectionDigest,
                    Error: string.IsNullOrWhiteSpace(blocker)
                        ? "manual_after_run_proposal_not_registered"
                        : "manual_after_run_proposal_ledger_corrupt");
            }

            Sr5AfterRunManualProposal[] identityMatches = ledger!.Proposals
                .Where(proposal => proposal.Projection.Identity == request.Identity)
                .Take(2)
                .ToArray();
            if (identityMatches.Length == 0)
            {
                return new(
                    CharacterAfterRunSettlementProposalProjectionOutcome.NotFound,
                    request.WorkspaceId,
                    request.WorkspaceRevision,
                    request.CharacterProjectionDigest,
                    Error: "manual_after_run_proposal_not_found");
            }
            if (identityMatches.Length != 1)
            {
                return new(
                    CharacterAfterRunSettlementProposalProjectionOutcome.Corrupt,
                    request.WorkspaceId,
                    request.WorkspaceRevision,
                    request.CharacterProjectionDigest,
                    Error: "duplicate_manual_after_run_proposal_identity");
            }
            Sr5AfterRunManualProposal proposal = identityMatches[0];
            if (proposal.WorkspaceId != request.WorkspaceId
                || proposal.WorkspaceRevision != request.WorkspaceRevision
                || !FixedEquals(
                    proposal.CharacterProjectionDigest,
                    request.CharacterProjectionDigest))
            {
                return new(
                    CharacterAfterRunSettlementProposalProjectionOutcome.Conflict,
                    request.WorkspaceId,
                    request.WorkspaceRevision,
                    request.CharacterProjectionDigest,
                    Error: "stale_or_foreign_manual_after_run_proposal");
            }
            return new(
                CharacterAfterRunSettlementProposalProjectionOutcome.Available,
                request.WorkspaceId,
                request.WorkspaceRevision,
                request.CharacterProjectionDigest,
                proposal.Projection);
        }
    }

    public Sr5AfterRunManualProposalPublishResult Publish(
        Sr5AfterRunManualProposalSubmission submission)
    {
        ArgumentNullException.ThrowIfNull(submission);
        lock (Gate)
        {
            if (!_snapshots.TryRead(
                    submission.WorkspaceId,
                    out Sr5AfterRunWorkspaceSnapshot snapshot,
                    out string blocker)
                || !snapshot.IsExact())
            {
                return Failed(string.IsNullOrWhiteSpace(blocker)
                    ? "The exact clean saved SR5 runner projection is unavailable."
                    : blocker);
            }
            if (snapshot.ContentRevision != submission.ExpectedWorkspaceRevision)
            {
                return Failed(
                    "The saved runner changed while the manual run result was open. Reopen it before approval.");
            }
            if (!TryCreateProposal(
                    snapshot,
                    submission,
                    out Sr5AfterRunManualProposal proposal,
                    out blocker))
            {
                return Failed(blocker);
            }

            Sr5AfterRunManualProposalLedger current;
            if (!TryReadLedgerLocked(
                    out Sr5AfterRunManualProposalLedger? existing,
                    out string readBlocker))
            {
                if (!string.IsNullOrWhiteSpace(readBlocker))
                {
                    return Failed(readBlocker);
                }
                current = Sr5AfterRunManualProposalIntegrity.CreateLedger(1, []);
            }
            else
            {
                current = existing!;
            }

            Sr5AfterRunManualProposal? sameIdentity = current.Proposals
                .SingleOrDefault(candidate =>
                    candidate.Projection.Identity == proposal.Projection.Identity);
            if (sameIdentity is not null)
            {
                if (FixedEquals(sameIdentity.ProposalDigest, proposal.ProposalDigest))
                {
                    return new(true, true, sameIdentity, string.Empty);
                }
                return Failed(
                    "That proposal, run, and character identity is already registered with different approved facts.");
            }

            Sr5AfterRunManualProposal[] retained = current.Proposals
                .Where(candidate => candidate.WorkspaceId != proposal.WorkspaceId
                    || candidate.WorkspaceRevision == proposal.WorkspaceRevision)
                .Append(proposal)
                .OrderBy(static candidate => candidate.WorkspaceId.Value, StringComparer.Ordinal)
                .ThenBy(static candidate => candidate.Projection.Identity.ProposalId)
                .ToArray();
            if (retained.Length > Sr5AfterRunProposalCatalogContract.MaximumProposalCount)
            {
                return Failed("The manual After Run proposal ledger is full.");
            }
            Sr5AfterRunManualProposalLedger next =
                Sr5AfterRunManualProposalIntegrity.CreateLedger(
                    checked(current.Version + 1),
                    retained);
            string payload = JsonSerializer.Serialize(next, JsonOptions);
            try
            {
                _backend.Write(payload);
                string readBack = _backend.Read();
                Sr5AfterRunManualProposalLedger? verified =
                    JsonSerializer.Deserialize<Sr5AfterRunManualProposalLedger>(
                        readBack,
                        JsonOptions);
                if (verified is null
                    || !verified.IsExact()
                    || verified.Version != next.Version
                    || !FixedEquals(verified.LedgerDigest, next.LedgerDigest)
                    || verified.Proposals.Count(candidate =>
                        FixedEquals(candidate.ProposalDigest, proposal.ProposalDigest)) != 1)
                {
                    return Failed(
                        "The approved manual After Run proposal did not survive exact durable write/read-back verification.");
                }
            }
            catch (Exception error) when (error is IOException
                or UnauthorizedAccessException
                or JsonException
                or NotSupportedException)
            {
                return Failed(
                    "The approved manual After Run proposal could not be stored durably.");
            }
            return new(true, false, proposal, string.Empty);
        }
    }

    private bool TryReadLedgerLocked(
        out Sr5AfterRunManualProposalLedger? ledger,
        out string blocker)
    {
        ledger = null;
        blocker = string.Empty;
        string payload;
        try
        {
            payload = _backend.Read();
        }
        catch (Exception error) when (error is IOException
            or UnauthorizedAccessException
            or NotSupportedException)
        {
            blocker = "The manual After Run proposal ledger is unavailable.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }
        try
        {
            ledger = JsonSerializer.Deserialize<Sr5AfterRunManualProposalLedger>(
                payload,
                JsonOptions);
        }
        catch (JsonException)
        {
            blocker = "The manual After Run proposal ledger is malformed and remains fail-closed.";
            return false;
        }
        if (ledger is null || !ledger.IsExact())
        {
            ledger = null;
            blocker = "The manual After Run proposal ledger failed its canonical digest and remains fail-closed.";
            return false;
        }
        return true;
    }

    private static bool TryCreateProposal(
        Sr5AfterRunWorkspaceSnapshot snapshot,
        Sr5AfterRunManualProposalSubmission submission,
        out Sr5AfterRunManualProposal proposal,
        out string blocker)
    {
        proposal = null!;
        blocker = string.Empty;
        CharacterAfterRunContactProposal[] contacts = submission.ContactProposals?
            .Select(static contact => contact is null
                ? null!
                : contact with
                {
                    Name = contact.Name.Trim(),
                    Role = contact.Role.Trim(),
                    Location = contact.Location.Trim()
                })
            .OrderBy(static contact => contact?.ContactId ?? Guid.Empty)
            .ToArray() ?? [];
        string runTitle = submission.RunTitle?.Trim() ?? string.Empty;
        string gmActor = submission.ExpectedGmActorId?.Trim() ?? string.Empty;
        string ownerActor = submission.ExpectedOwnerActorId?.Trim() ?? string.Empty;
        string gmReason = submission.GmReviewReason?.Trim() ?? string.Empty;
        string ownerReason = submission.OwnerReviewReason?.Trim() ?? string.Empty;
        string rewardDigest = submission.RewardReceiptDigest?.Trim().ToLowerInvariant()
            ?? string.Empty;
        CharacterAfterRunSettlementIdentity? identity = submission.Identity;
        if (!ValidIdentity(identity)
            || runTitle.Length is 0 or > CharacterAfterRunSettlementRules.MaximumTextLength
            || submission.CompletedAt == default
            || submission.KarmaAward is < 0 or > CharacterAfterRunSettlementRules.MaximumValue
            || submission.NuyenAward is < 0m or > CharacterAfterRunSettlementRules.MaximumValue
            || !CharacterAfterRunSettlementRules.IsCanonicalDigest(rewardDigest)
            || !submission.TargetOwnedByCharacter
            || !submission.RunCompleted
            || submission.CurrentHeat is < 0 or > CharacterAfterRunSettlementRules.MaximumValue
            || submission.HeatDelta is < -CharacterAfterRunSettlementRules.MaximumValue
                or > CharacterAfterRunSettlementRules.MaximumValue
            || submission.StreetCredDelta is < -CharacterAfterRunSettlementRules.MaximumValue
                or > CharacterAfterRunSettlementRules.MaximumValue
            || submission.NotorietyDelta is < -CharacterAfterRunSettlementRules.MaximumValue
                or > CharacterAfterRunSettlementRules.MaximumValue
            || submission.PublicAwarenessDelta is < -CharacterAfterRunSettlementRules.MaximumValue
                or > CharacterAfterRunSettlementRules.MaximumValue
            || !ValidSettings(submission.Settings)
            || contacts.Length > CharacterAfterRunSettlementRules.MaximumContactCount
            || contacts.Any(static contact => !ValidContact(contact))
            || contacts.Select(static contact => contact.ContactId).Distinct().Count()
                != contacts.Length
            || !ValidActorId(gmActor)
            || !ValidActorId(ownerActor)
            || string.Equals(gmActor, ownerActor, StringComparison.Ordinal)
            || submission.GmReviewId == Guid.Empty
            || submission.OwnerReviewId == Guid.Empty
            || submission.GmReviewId == submission.OwnerReviewId
            || gmReason.Length > CharacterAfterRunSettlementRules.MaximumTextLength
            || ownerReason.Length > CharacterAfterRunSettlementRules.MaximumTextLength
            || !submission.GmApproved
            || !submission.OwnerApproved)
        {
            blocker = "Enter exact run/reward/consequence/policy/contact identities and record both explicit approvals before publishing.";
            return false;
        }

        var reward = Sr5AfterRunRewardContext.Create(
            identity!,
            runTitle,
            submission.CompletedAt.ToUniversalTime(),
            submission.KarmaAward,
            submission.NuyenAward,
            rewardDigest);
        var gmReview = new CharacterAfterRunReview(
            submission.GmReviewId,
            CharacterAfterRunReviewRole.GameMaster,
            gmActor,
            CharacterAfterRunReviewDecision.Approved,
            gmReason);
        var ownerReview = new CharacterAfterRunReview(
            submission.OwnerReviewId,
            CharacterAfterRunReviewRole.CharacterOwner,
            ownerActor,
            CharacterAfterRunReviewDecision.Approved,
            ownerReason);
        string digest = Sr5AfterRunManualProposalIntegrity.ComputeProposalDigest(
            snapshot,
            reward,
            submission.CurrentHeat,
            submission.HeatDelta,
            submission.StreetCredDelta,
            submission.NotorietyDelta,
            submission.PublicAwarenessDelta,
            submission.Settings,
            contacts,
            gmReview,
            ownerReview);
        var projection = new CharacterAfterRunSettlementProposalProjection(
            identity,
            TargetOwnedByCharacter: true,
            ProjectionIsExact: true,
            RunCompleted: true,
            gmActor,
            ownerActor,
            submission.CurrentHeat,
            submission.HeatDelta,
            submission.StreetCredDelta,
            submission.NotorietyDelta,
            submission.PublicAwarenessDelta,
            submission.Settings,
            contacts,
            gmReview,
            ownerReview,
            Sr5AfterRunManualProposalIntegrity.RawState("source", digest),
            Sr5AfterRunManualProposalIntegrity.RawState("custom-data", digest),
            Sr5AfterRunManualProposalIntegrity.RawState("gm-policy", digest),
            Sr5AfterRunManualProposalIntegrity.RawState("runtime", digest));
        var candidate = new Sr5AfterRunManualProposal(
            Sr5AfterRunManualProposal.CurrentSchemaVersion,
            snapshot.WorkspaceId,
            snapshot.ContentRevision,
            snapshot.CharacterProjectionDigest,
            reward,
            projection,
            digest);
        var input = new CharacterAfterRunSettlementInput(
            identity,
            snapshot.Created,
            snapshot.RulesetId,
            projection.TargetOwnedByCharacter,
            projection.ProjectionIsExact,
            projection.RunCompleted,
            ProposalAlreadySettled: false,
            projection.ExpectedGmActorId,
            projection.ExpectedOwnerActorId,
            projection.CurrentHeat,
            snapshot.CurrentStreetCred,
            snapshot.CurrentNotoriety,
            snapshot.CurrentPublicAwareness,
            snapshot.CurrentKarma,
            projection.HeatDelta,
            projection.StreetCredDelta,
            projection.NotorietyDelta,
            projection.PublicAwarenessDelta,
            projection.Settings,
            projection.ContactProposals,
            projection.GmReview,
            projection.OwnerReview,
            projection.RawSourceState,
            projection.RawCustomDataState,
            projection.RawGmPolicyState,
            projection.RawRuntimeState);
        bool candidateIsExact = candidate.IsExact();
        bool quoteCreated = CharacterAfterRunSettlementRules.TryCreateQuote(
            input,
            out CharacterAfterRunSettlementQuote quote);
        if (!candidateIsExact
            || !quoteCreated
            || !quote.CanSettle)
        {
            blocker = quote is not null
                && CharacterAfterRunSettlementRules.IsCoherent(quote)
                && !quote.CanSettle
                    ? Sr5AfterRunSettlementDraft.BlockerText(quote.Blocker)
                    : "Core rejected the fully combined manual After Run proposal projection.";
            return false;
        }
        proposal = candidate;
        return true;
    }

    private static Sr5AfterRunManualProposalPublishResult Failed(string blocker)
        => new(false, false, null, blocker);

    private static bool MatchesSnapshot(
        Sr5AfterRunManualProposal proposal,
        Sr5AfterRunWorkspaceSnapshot snapshot)
        => proposal.IsExact()
            && proposal.WorkspaceId == snapshot.WorkspaceId
            && proposal.WorkspaceRevision == snapshot.ContentRevision
            && FixedEquals(
                proposal.CharacterProjectionDigest,
                snapshot.CharacterProjectionDigest);

    private static bool ValidIdentity(CharacterAfterRunSettlementIdentity? identity)
        => identity is not null
            && identity.ProposalId != Guid.Empty
            && identity.RunId != Guid.Empty
            && identity.CharacterId != Guid.Empty
            && identity.ProposalId != identity.RunId
            && identity.ProposalId != identity.CharacterId;

    internal static bool ValidSettings(CharacterAfterRunSettlementSettings? settings)
        => settings is not null
            && settings.MaximumHeat is >= 0 and <= CharacterAfterRunSettlementRules.MaximumValue
            && settings.MaximumReputation is >= 0 and <= CharacterAfterRunSettlementRules.MaximumValue
            && settings.MaximumConnection is >= 1 and <= CharacterAfterRunSettlementRules.MaximumValue
            && settings.MaximumLoyalty is >= 1 and <= CharacterAfterRunSettlementRules.MaximumValue
            && settings.KarmaPerContactPoint is >= 0 and <= CharacterAfterRunSettlementRules.MaximumValue;

    internal static bool ValidContact(CharacterAfterRunContactProposal? contact)
        => contact is not null
            && contact.ContactId != Guid.Empty
            && contact.Name is { Length: > 0 and <= CharacterAfterRunSettlementRules.MaximumTextLength }
            && contact.Role is { Length: <= CharacterAfterRunSettlementRules.MaximumTextLength }
            && contact.Location is { Length: <= CharacterAfterRunSettlementRules.MaximumTextLength }
            && contact.Connection is >= 1 and <= CharacterAfterRunSettlementRules.MaximumValue
            && contact.Loyalty is >= 1 and <= CharacterAfterRunSettlementRules.MaximumValue
            && Enum.IsDefined(contact.Kind);

    internal static bool ValidActorId(string? actorId)
        => actorId is { Length: > 0 and <= CharacterAfterRunSettlementRules.MaximumTextLength }
            && actorId.All(static character =>
                char.IsLetterOrDigit(character) || character is '-' or '_' or '.');

    internal static bool FixedEquals(string? left, string? right)
    {
        if (left is null || right is null)
        {
            return false;
        }
        byte[] leftBytes = Encoding.UTF8.GetBytes(left);
        byte[] rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length
            && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}

internal static class Sr5AfterRunManualProposalIntegrity
{
    private const string ContractName =
        "chummer.android.sr5-manual-after-run-proposal/v1";

    public static Sr5AfterRunManualProposalLedger CreateLedger(
        long version,
        IReadOnlyList<Sr5AfterRunManualProposal> proposals)
    {
        var unsigned = new Sr5AfterRunManualProposalLedger(
            Sr5AfterRunManualProposalLedger.CurrentSchemaVersion,
            version,
            proposals,
            string.Empty);
        return unsigned with { LedgerDigest = ComputeLedgerDigest(unsigned) };
    }

    public static bool IsExact(Sr5AfterRunManualProposalLedger? ledger)
        => ledger is not null
            && ledger.SchemaVersion == Sr5AfterRunManualProposalLedger.CurrentSchemaVersion
            && ledger.Version > 0
            && ledger.Proposals is not null
            && ledger.Proposals.Count <= Sr5AfterRunProposalCatalogContract.MaximumProposalCount
            && ledger.Proposals.All(static proposal => proposal is not null && proposal.IsExact())
            && ledger.Proposals.Select(static proposal => proposal.Projection.Identity)
                .Distinct().Count() == ledger.Proposals.Count
            && ledger.Proposals.SequenceEqual(ledger.Proposals
                .OrderBy(static proposal => proposal.WorkspaceId.Value, StringComparer.Ordinal)
                .ThenBy(static proposal => proposal.Projection.Identity.ProposalId))
            && CharacterAfterRunSettlementRules.IsCanonicalDigest(ledger.LedgerDigest)
            && Sr5AfterRunManualProposalSource.FixedEquals(
                ledger.LedgerDigest,
                ComputeLedgerDigest(ledger));

    public static bool IsExact(Sr5AfterRunManualProposal? proposal)
    {
        if (proposal is null
            || proposal.SchemaVersion != Sr5AfterRunManualProposal.CurrentSchemaVersion
            || !CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(
                proposal.WorkspaceId)
            || proposal.WorkspaceRevision <= 0
            || !CharacterAfterRunSettlementRules.IsCanonicalDigest(
                proposal.CharacterProjectionDigest)
            || proposal.RewardContext is null
            || !proposal.RewardContext.IsExact()
            || proposal.Projection is null
            || proposal.RewardContext.Identity != proposal.Projection.Identity
            || !proposal.Projection.TargetOwnedByCharacter
            || !proposal.Projection.ProjectionIsExact
            || !proposal.Projection.RunCompleted
            || !Sr5AfterRunManualProposalSource.ValidActorId(
                proposal.Projection.ExpectedGmActorId)
            || !Sr5AfterRunManualProposalSource.ValidActorId(
                proposal.Projection.ExpectedOwnerActorId)
            || string.Equals(
                proposal.Projection.ExpectedGmActorId,
                proposal.Projection.ExpectedOwnerActorId,
                StringComparison.Ordinal)
            || proposal.Projection.CurrentHeat is < 0
                or > CharacterAfterRunSettlementRules.MaximumValue
            || proposal.Projection.HeatDelta is < -CharacterAfterRunSettlementRules.MaximumValue
                or > CharacterAfterRunSettlementRules.MaximumValue
            || proposal.Projection.StreetCredDelta is < -CharacterAfterRunSettlementRules.MaximumValue
                or > CharacterAfterRunSettlementRules.MaximumValue
            || proposal.Projection.NotorietyDelta is < -CharacterAfterRunSettlementRules.MaximumValue
                or > CharacterAfterRunSettlementRules.MaximumValue
            || proposal.Projection.PublicAwarenessDelta is < -CharacterAfterRunSettlementRules.MaximumValue
                or > CharacterAfterRunSettlementRules.MaximumValue
            || !Sr5AfterRunManualProposalSource.ValidSettings(proposal.Projection.Settings)
            || proposal.Projection.ContactProposals is null
            || proposal.Projection.ContactProposals.Count
                > CharacterAfterRunSettlementRules.MaximumContactCount
            || proposal.Projection.ContactProposals.Any(static contact =>
                !Sr5AfterRunManualProposalSource.ValidContact(contact))
            || proposal.Projection.ContactProposals.Select(static contact => contact.ContactId)
                .Distinct().Count() != proposal.Projection.ContactProposals.Count
            || !proposal.Projection.ContactProposals.SequenceEqual(
                proposal.Projection.ContactProposals.OrderBy(static contact => contact.ContactId))
            || !ApprovedReview(
                proposal.Projection.GmReview,
                CharacterAfterRunReviewRole.GameMaster,
                proposal.Projection.ExpectedGmActorId)
            || !ApprovedReview(
                proposal.Projection.OwnerReview,
                CharacterAfterRunReviewRole.CharacterOwner,
                proposal.Projection.ExpectedOwnerActorId)
            || proposal.Projection.GmReview!.ReviewId
                == proposal.Projection.OwnerReview!.ReviewId
            || !CharacterAfterRunSettlementRules.IsCanonicalDigest(
                proposal.ProposalDigest))
        {
            return false;
        }

        string expected = ComputeProposalDigest(
            new Sr5AfterRunWorkspaceSnapshot(
                proposal.WorkspaceId,
                proposal.WorkspaceRevision,
                proposal.WorkspaceRevision,
                CharacterAfterRunSettlementRules.RulesetId,
                Created: true,
                CurrentStreetCred: 0,
                CurrentNotoriety: 0,
                CurrentPublicAwareness: 0,
                CurrentKarma: 0,
                proposal.CharacterProjectionDigest),
            proposal.RewardContext,
            proposal.Projection.CurrentHeat,
            proposal.Projection.HeatDelta,
            proposal.Projection.StreetCredDelta,
            proposal.Projection.NotorietyDelta,
            proposal.Projection.PublicAwarenessDelta,
            proposal.Projection.Settings,
            proposal.Projection.ContactProposals,
            proposal.Projection.GmReview,
            proposal.Projection.OwnerReview);
        return Sr5AfterRunManualProposalSource.FixedEquals(
                proposal.ProposalDigest,
                expected)
            && string.Equals(
                proposal.Projection.RawSourceState,
                RawState("source", expected),
                StringComparison.Ordinal)
            && string.Equals(
                proposal.Projection.RawCustomDataState,
                RawState("custom-data", expected),
                StringComparison.Ordinal)
            && string.Equals(
                proposal.Projection.RawGmPolicyState,
                RawState("gm-policy", expected),
                StringComparison.Ordinal)
            && string.Equals(
                proposal.Projection.RawRuntimeState,
                RawState("runtime", expected),
                StringComparison.Ordinal);
    }

    public static string ComputeProposalDigest(
        Sr5AfterRunWorkspaceSnapshot snapshot,
        Sr5AfterRunRewardContext reward,
        int currentHeat,
        int heatDelta,
        int streetCredDelta,
        int notorietyDelta,
        int publicAwarenessDelta,
        CharacterAfterRunSettlementSettings settings,
        IReadOnlyList<CharacterAfterRunContactProposal> contacts,
        CharacterAfterRunReview gmReview,
        CharacterAfterRunReview ownerReview)
    {
        var canonical = new StringBuilder();
        Add(canonical, ContractName);
        Add(canonical, snapshot.WorkspaceId.Value);
        Add(canonical, snapshot.ContentRevision.ToString(CultureInfo.InvariantCulture));
        Add(canonical, snapshot.CharacterProjectionDigest);
        Add(canonical, reward.Identity.ProposalId.ToString("D"));
        Add(canonical, reward.Identity.RunId.ToString("D"));
        Add(canonical, reward.Identity.CharacterId.ToString("D"));
        Add(canonical, reward.RunTitle);
        Add(canonical, reward.CompletedAt.ToUniversalTime().ToString("O"));
        Add(canonical, reward.KarmaAward.ToString(CultureInfo.InvariantCulture));
        Add(canonical, reward.NuyenAward.ToString("G29", CultureInfo.InvariantCulture));
        Add(canonical, reward.RewardReceiptDigest);
        Add(canonical, currentHeat.ToString(CultureInfo.InvariantCulture));
        Add(canonical, heatDelta.ToString(CultureInfo.InvariantCulture));
        Add(canonical, streetCredDelta.ToString(CultureInfo.InvariantCulture));
        Add(canonical, notorietyDelta.ToString(CultureInfo.InvariantCulture));
        Add(canonical, publicAwarenessDelta.ToString(CultureInfo.InvariantCulture));
        Add(canonical, settings.MaximumHeat.ToString(CultureInfo.InvariantCulture));
        Add(canonical, settings.MaximumReputation.ToString(CultureInfo.InvariantCulture));
        Add(canonical, settings.MaximumConnection.ToString(CultureInfo.InvariantCulture));
        Add(canonical, settings.MaximumLoyalty.ToString(CultureInfo.InvariantCulture));
        Add(canonical, settings.KarmaPerContactPoint.ToString(CultureInfo.InvariantCulture));
        Add(canonical, settings.AllowRunRewardContacts ? "true" : "false");
        Add(canonical, settings.AllowKarmaPurchasedContacts ? "true" : "false");
        Add(canonical, settings.UseCalculatedPublicAwareness ? "true" : "false");
        foreach (CharacterAfterRunContactProposal contact in contacts
            .OrderBy(static value => value.ContactId))
        {
            Add(canonical, contact.ContactId.ToString("D"));
            Add(canonical, contact.Name);
            Add(canonical, contact.Role);
            Add(canonical, contact.Location);
            Add(canonical, contact.Connection.ToString(CultureInfo.InvariantCulture));
            Add(canonical, contact.Loyalty.ToString(CultureInfo.InvariantCulture));
            Add(canonical, contact.Kind.ToString());
        }
        AddReview(canonical, gmReview);
        AddReview(canonical, ownerReview);
        return Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    public static string RawState(string authority, string proposalDigest)
        => $"{ContractName}/{authority}/{proposalDigest}";

    private static string ComputeLedgerDigest(
        Sr5AfterRunManualProposalLedger ledger)
    {
        var canonical = new StringBuilder();
        Add(canonical, $"{ContractName}/ledger");
        Add(canonical, ledger.SchemaVersion.ToString(CultureInfo.InvariantCulture));
        Add(canonical, ledger.Version.ToString(CultureInfo.InvariantCulture));
        foreach (Sr5AfterRunManualProposal proposal in ledger.Proposals)
        {
            Add(canonical, proposal.WorkspaceId.Value);
            Add(canonical, proposal.WorkspaceRevision.ToString(CultureInfo.InvariantCulture));
            Add(canonical, proposal.Projection.Identity.ProposalId.ToString("D"));
            Add(canonical, proposal.ProposalDigest);
        }
        return Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private static bool ApprovedReview(
        CharacterAfterRunReview? review,
        CharacterAfterRunReviewRole role,
        string actorId)
        => review is not null
            && review.ReviewId != Guid.Empty
            && review.Role == role
            && string.Equals(review.ActorId, actorId, StringComparison.Ordinal)
            && review.Decision == CharacterAfterRunReviewDecision.Approved
            && review.Reason is { Length: <= CharacterAfterRunSettlementRules.MaximumTextLength };

    private static void AddReview(
        StringBuilder canonical,
        CharacterAfterRunReview review)
    {
        Add(canonical, review.ReviewId.ToString("D"));
        Add(canonical, review.Role.ToString());
        Add(canonical, review.ActorId);
        Add(canonical, review.Decision.ToString());
        Add(canonical, review.Reason);
    }

    private static void Add(StringBuilder canonical, string value)
    {
        string actual = value ?? string.Empty;
        canonical.Append(actual.Length.ToString(CultureInfo.InvariantCulture));
        canonical.Append(':');
        canonical.Append(actual);
        canonical.Append('\n');
    }
}
