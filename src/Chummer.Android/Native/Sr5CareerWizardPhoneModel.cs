using System.Security.Cryptography;
using System.Text;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public static class Sr5CareerWizardPhoneBlockers
{
    public const string NoEligibleTarget = "career-wizard-no-eligible-target";
    public const string WorkspaceAuthorityUnavailable =
        "career-wizard-workspace-authority-unavailable";
    public const string WorkspaceChangedDuringProjection =
        "career-wizard-workspace-changed-during-projection";
}

public sealed record Sr5CareerWizardPhoneWorkspaceAuthority(
    string WorkspaceId,
    long WorkspaceRevision,
    long SavedRevision,
    string RulesetId,
    string PayloadSha256,
    string DocumentSha256);

/// <summary>
/// One renderer-side observation of an existing typed action presenter. The digest is over
/// the typed, read-only projection returned by that presenter; this type carries no command,
/// confirmation, idempotency key, mutation payload, or receipt.
/// </summary>
public sealed record Sr5CareerWizardPhoneAuthorityProbe(
    string ActionId,
    bool IsAvailable,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> SourceAnchorIds,
    string ProjectionDigest);

public sealed record Sr5CareerWizardPhoneActionDefinition(
    string ActionId,
    string FamilyId,
    string Title,
    string Detail,
    string AutomationId,
    string RouteId);

public sealed record Sr5CareerWizardPhoneFamilyDefinition(
    string FamilyId,
    string Title,
    string Detail,
    string AutomationId,
    string RouteId);

/// <summary>
/// Closed native routing catalog for the renderer-neutral Presentation session. Every entry
/// points at an already existing typed Career page; there is intentionally no fallback route.
/// </summary>
public static class Sr5CareerWizardPhoneCatalog
{
    private const string RuntimeSchema = "chummer.android.sr5-career-wizard.runtime.v1";

    public static IReadOnlyList<Sr5CareerWizardPhoneFamilyDefinition> Families { get; } =
    [
        new(
            Sr5CareerWizardFamilyIds.Economy,
            "Karma and Nuyen",
            "Record exact manual changes or edit existing typed expense entries.",
            "sr5-career-family-economy",
            "sr5-career/economy"),
        new(
            Sr5CareerWizardFamilyIds.Advancement,
            "Advancement",
            "Open one exact SR5 advancement authority and review it in its own wizard.",
            "sr5-career-family-advancement",
            "sr5-career/advancement"),
        new(
            Sr5CareerWizardFamilyIds.Calendar,
            "Calendar",
            "Manage exact saved downtime weeks by stable identity.",
            "sr5-career-family-calendar",
            "sr5-career/calendar")
    ];

    public static IReadOnlyList<Sr5CareerWizardPhoneActionDefinition> Actions { get; } =
    [
        new(
            Sr5CareerWizardActionIds.AdjustKarma,
            Sr5CareerWizardFamilyIds.Economy,
            "Record Karma",
            "Open the typed manual Karma editor for this exact runner revision.",
            "sr5-career-action-karma",
            "sr5-career/economy/karma"),
        new(
            Sr5CareerWizardActionIds.AdjustNuyen,
            Sr5CareerWizardFamilyIds.Economy,
            "Record Nuyen",
            "Open the typed manual Nuyen editor for this exact runner revision.",
            "sr5-career-action-nuyen",
            "sr5-career/economy/nuyen"),
        new(
            Sr5CareerWizardActionIds.EditKarmaExpense,
            Sr5CareerWizardFamilyIds.Economy,
            "Edit Karma expense",
            "Choose one stable saved Karma expense and edit only Chummer5-authorized fields.",
            "sr5-career-action-karma-expense",
            "sr5-career/economy/karma-expense"),
        new(
            Sr5CareerWizardActionIds.EditNuyenExpense,
            Sr5CareerWizardFamilyIds.Economy,
            "Edit Nuyen expense",
            "Choose one stable saved Nuyen expense and edit only Chummer5-authorized fields.",
            "sr5-career-action-nuyen-expense",
            "sr5-career/economy/nuyen-expense"),
        new(
            Sr5CareerWizardActionIds.AdvanceAttribute,
            Sr5CareerWizardFamilyIds.Advancement,
            "Advance an attribute",
            "Choose an exact Core quote, review Karma and legality, then enter its typed flow.",
            "sr5-career-action-attribute",
            "sr5-career/advancement/attribute/choose"),
        new(
            Sr5CareerWizardActionIds.AdvanceActiveSkill,
            Sr5CareerWizardFamilyIds.Advancement,
            "Advance an active skill",
            "Choose an exact skill identity and enter the existing quote/review/receipt flow.",
            "sr5-career-action-active-skill",
            "sr5-career/advancement/active-skill/choose"),
        new(
            Sr5CareerWizardActionIds.AdvanceKnowledgeSkill,
            Sr5CareerWizardFamilyIds.Advancement,
            "Advance Knowledge or Language",
            "Choose an exact nullable-source identity and enter its typed advancement flow.",
            "sr5-career-action-knowledge-language",
            "sr5-career/advancement/knowledge-language/choose"),
        new(
            Sr5CareerWizardActionIds.AdvanceSkillGroup,
            Sr5CareerWizardFamilyIds.Advancement,
            "Advance a skill group",
            "Choose an exact InternalId and enter the Core-bound group flow.",
            "sr5-career-action-skill-group",
            "sr5-career/advancement/skill-group/choose"),
        new(
            Sr5CareerWizardActionIds.LearnSpecialization,
            Sr5CareerWizardFamilyIds.Advancement,
            "Add a specialization",
            "Choose a typed skill identity and enter the governed/custom specialization flow.",
            "sr5-career-action-specialization",
            "sr5-career/advancement/specialization/choose"),
        new(
            Sr5CareerWizardActionIds.ChangeQuality,
            Sr5CareerWizardFamilyIds.Advancement,
            "Change a quality",
            "Choose an exact source/identity operation and enter the atomic quality flow.",
            "sr5-career-action-quality",
            "sr5-career/advancement/quality/choose"),
        new(
            Sr5CareerWizardActionIds.ManageCalendarEntry,
            Sr5CareerWizardFamilyIds.Calendar,
            "Manage calendar weeks",
            "Add, edit, or delete exact saved ISO weeks by stable identity.",
            "sr5-career-action-calendar",
            "sr5-career/calendar/manage")
    ];

    private static readonly IReadOnlyDictionary<string, Sr5CareerWizardPhoneActionDefinition>
        ActionsById = Actions.ToDictionary(static action => action.ActionId, StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, Sr5CareerWizardPhoneFamilyDefinition>
        FamiliesById = Families.ToDictionary(static family => family.FamilyId, StringComparer.Ordinal);

    static Sr5CareerWizardPhoneCatalog()
    {
        if (!Actions.Select(static action => action.ActionId).SequenceEqual(
                Sr5CareerWizardProjector.KnownActionIds,
                StringComparer.Ordinal)
            || Actions.Any(action => !FamiliesById.ContainsKey(action.FamilyId))
            || Actions.Select(static action => action.AutomationId).Distinct(StringComparer.Ordinal).Count()
                != Actions.Count
            || Actions.Select(static action => action.RouteId).Distinct(StringComparer.Ordinal).Count()
                != Actions.Count)
        {
            throw new InvalidOperationException(
                "The native SR5 Career route catalog does not match Presentation authority.");
        }
    }

    public static string RuntimeFingerprint { get; } = ComputeRuntimeFingerprint();

    public static Sr5CareerWizardPhoneActionDefinition RequireAction(string actionId)
        => ActionsById.TryGetValue(actionId, out Sr5CareerWizardPhoneActionDefinition? action)
            ? action
            : throw new InvalidOperationException("Unknown SR5 Career action route.");

    public static Sr5CareerWizardPhoneFamilyDefinition RequireFamily(string familyId)
        => FamiliesById.TryGetValue(familyId, out Sr5CareerWizardPhoneFamilyDefinition? family)
            ? family
            : throw new InvalidOperationException("Unknown SR5 Career action family route.");

    private static string ComputeRuntimeFingerprint()
    {
        var canonical = new StringBuilder(RuntimeSchema);
        foreach (Sr5CareerWizardPhoneFamilyDefinition family in Families)
        {
            Sr5CareerWizardPhoneProjection.Append(canonical, family.FamilyId);
            Sr5CareerWizardPhoneProjection.Append(canonical, family.AutomationId);
            Sr5CareerWizardPhoneProjection.Append(canonical, family.RouteId);
        }
        foreach (Sr5CareerWizardPhoneActionDefinition action in Actions)
        {
            Sr5CareerWizardPhoneProjection.Append(canonical, action.ActionId);
            Sr5CareerWizardPhoneProjection.Append(canonical, action.FamilyId);
            Sr5CareerWizardPhoneProjection.Append(canonical, action.AutomationId);
            Sr5CareerWizardPhoneProjection.Append(canonical, action.RouteId);
        }
        return Sr5CareerWizardPhoneProjection.Hash(canonical.ToString());
    }
}

public static class Sr5CareerWizardPhoneProjection
{
    private const string ContentDigestSchema = "chummer.android.sr5-career-wizard.content.v1";
    private const string SourceDigestSchema = "chummer.android.sr5-career-wizard.sources.v1";
    private const string AuthorityDigestSchema = "chummer.android.sr5-career-wizard.authority.v1";

    public static Sr5CareerWizardSnapshot Project(
        Sr5CareerWizardPhoneWorkspaceAuthority workspace,
        IReadOnlyList<Sr5CareerWizardPhoneAuthorityProbe> probes)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(probes);
        ValidateWorkspace(workspace);

        var canonical = new Dictionary<string, Sr5CareerWizardPhoneAuthorityProbe>(StringComparer.Ordinal);
        foreach (Sr5CareerWizardPhoneAuthorityProbe? probe in probes)
        {
            if (probe is null
                || !Sr5CareerWizardProjector.KnownActionIds.Contains(probe.ActionId, StringComparer.Ordinal)
                || !canonical.TryAdd(probe.ActionId, Canonicalize(probe)))
            {
                throw new InvalidOperationException(
                    "Native SR5 Career probes must be non-null, known, and uniquely identified.");
            }
        }

        string contentDigest = ComputeContentDigest(workspace);
        string sourceDigest = ComputeSourceDigest(canonical);
        var binding = new Sr5CareerWizardBinding(
            workspace.WorkspaceId,
            workspace.WorkspaceRevision,
            workspace.SavedRevision,
            workspace.RulesetId,
            Sr5CareerWizardPhoneCatalog.RuntimeFingerprint,
            sourceDigest,
            contentDigest);

        Sr5CareerWizardAuthorityAvailability[] authorities = Sr5CareerWizardPhoneCatalog.Actions
            .Where(action => canonical.ContainsKey(action.ActionId))
            .Select(action => ToAvailability(binding, action, canonical[action.ActionId]))
            .ToArray();
        return Sr5CareerWizardProjector.Project(binding, authorities);
    }

    public static string DigestProjection(ReadOnlySpan<byte> payload)
        => "sha256:" + Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();

    internal static string Hash(string value)
        => DigestProjection(Encoding.UTF8.GetBytes(value));

    internal static void Append(StringBuilder builder, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        builder.Append(value.Length)
            .Append(':')
            .Append(value)
            .Append(';');
    }

    private static Sr5CareerWizardPhoneAuthorityProbe Canonicalize(
        Sr5CareerWizardPhoneAuthorityProbe probe)
    {
        if (!IsCanonicalDigest(probe.ProjectionDigest)
            || probe.Blockers is null
            || probe.SourceAnchorIds is null
            || probe.Blockers.Any(string.IsNullOrWhiteSpace)
            || probe.SourceAnchorIds.Any(string.IsNullOrWhiteSpace)
            || probe.Blockers.Distinct(StringComparer.Ordinal).Count() != probe.Blockers.Count
            || probe.SourceAnchorIds.Distinct(StringComparer.Ordinal).Count()
                != probe.SourceAnchorIds.Count
            || probe.IsAvailable == probe.Blockers.Any())
        {
            throw new InvalidOperationException(
                "A native SR5 Career probe must have a coherent digest and availability state.");
        }
        return probe with
        {
            Blockers = probe.Blockers.Order(StringComparer.Ordinal).ToArray(),
            SourceAnchorIds = probe.SourceAnchorIds.Order(StringComparer.Ordinal).ToArray()
        };
    }

    private static Sr5CareerWizardAuthorityAvailability ToAvailability(
        Sr5CareerWizardBinding binding,
        Sr5CareerWizardPhoneActionDefinition action,
        Sr5CareerWizardPhoneAuthorityProbe probe)
    {
        var material = new StringBuilder(AuthorityDigestSchema);
        AppendBinding(material, binding);
        Append(material, action.ActionId);
        Append(material, action.FamilyId);
        Append(material, action.RouteId);
        Append(material, probe.IsAvailable ? "1" : "0");
        Append(material, probe.ProjectionDigest);
        foreach (string blocker in probe.Blockers)
            Append(material, blocker);
        foreach (string sourceAnchorId in probe.SourceAnchorIds)
            Append(material, sourceAnchorId);
        return new Sr5CareerWizardAuthorityAvailability(
            binding,
            action.ActionId,
            probe.IsAvailable,
            probe.Blockers,
            probe.SourceAnchorIds,
            Hash(material.ToString()));
    }

    private static string ComputeContentDigest(Sr5CareerWizardPhoneWorkspaceAuthority workspace)
    {
        var material = new StringBuilder(ContentDigestSchema);
        Append(material, workspace.PayloadSha256);
        Append(material, workspace.DocumentSha256);
        return Hash(material.ToString());
    }

    private static string ComputeSourceDigest(
        IReadOnlyDictionary<string, Sr5CareerWizardPhoneAuthorityProbe> probes)
    {
        var material = new StringBuilder(SourceDigestSchema);
        foreach (Sr5CareerWizardPhoneActionDefinition action in Sr5CareerWizardPhoneCatalog.Actions)
        {
            Append(material, action.ActionId);
            if (!probes.TryGetValue(action.ActionId, out Sr5CareerWizardPhoneAuthorityProbe? probe))
            {
                Append(material, "missing");
                continue;
            }
            Append(material, probe.IsAvailable ? "available" : "blocked");
            Append(material, probe.ProjectionDigest);
            foreach (string blocker in probe.Blockers)
                Append(material, blocker);
            foreach (string sourceAnchorId in probe.SourceAnchorIds)
                Append(material, sourceAnchorId);
        }
        return Hash(material.ToString());
    }

    private static void AppendBinding(StringBuilder material, Sr5CareerWizardBinding binding)
    {
        Append(material, binding.WorkspaceId);
        Append(material, binding.WorkspaceRevision.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Append(material, binding.SavedRevision.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Append(material, binding.RulesetId);
        Append(material, binding.RuntimeFingerprint);
        Append(material, binding.SourceDigest);
        Append(material, binding.ContentDigest);
    }

    private static void ValidateWorkspace(Sr5CareerWizardPhoneWorkspaceAuthority workspace)
    {
        if (string.IsNullOrWhiteSpace(workspace.WorkspaceId)
            || workspace.WorkspaceRevision <= 0
            || workspace.SavedRevision < 0
            || !string.Equals(workspace.RulesetId, "sr5", StringComparison.Ordinal)
            || !IsRawSha256(workspace.PayloadSha256)
            || !IsRawSha256(workspace.DocumentSha256))
        {
            throw new InvalidOperationException(
                "The native SR5 Career chooser requires exact workspace and document authority.");
        }
    }

    private static bool IsRawSha256(string? value)
        => value is { Length: 64 }
           && value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsCanonicalDigest(string? value)
        => value is { Length: 71 }
           && value.StartsWith("sha256:", StringComparison.Ordinal)
           && IsRawSha256(value["sha256:".Length..]);
}

public interface ISr5CareerWizardPhoneCheckpointBackend
{
    string Read();
    void Write(string payload);
    void Remove();
}

public enum Sr5CareerWizardPhoneCheckpointReadStatus
{
    Empty,
    Ready,
    Invalid,
    Unavailable
}

public sealed record Sr5CareerWizardPhoneCheckpointRead(
    Sr5CareerWizardPhoneCheckpointReadStatus Status,
    Sr5CareerWizardCheckpoint? Checkpoint);

/// <summary>
/// Durable navigation-only checkpoint store. It never stores a quote, plan, command, confirmation,
/// idempotency key, mutation payload, or receipt, and every write is verified by exact read-back.
/// </summary>
public sealed class Sr5CareerWizardPhoneCheckpointStore
{
    private const int MaximumPayloadBytes = 4096;
    private const int MaximumEncodedCharacters = 8192;
    private readonly ISr5CareerWizardPhoneCheckpointBackend _backend;
    private readonly object _sync = new();

    public Sr5CareerWizardPhoneCheckpointStore(ISr5CareerWizardPhoneCheckpointBackend backend)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    public Sr5CareerWizardPhoneCheckpointRead Read()
    {
        lock (_sync)
        {
            string encoded;
            try
            {
                encoded = _backend.Read();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                return new(Sr5CareerWizardPhoneCheckpointReadStatus.Unavailable, null);
            }
            if (string.IsNullOrEmpty(encoded))
                return new(Sr5CareerWizardPhoneCheckpointReadStatus.Empty, null);
            if (encoded.Length > MaximumEncodedCharacters)
                return InvalidAndRemove();

            byte[] payload;
            try
            {
                payload = Convert.FromBase64String(encoded);
            }
            catch (FormatException)
            {
                return InvalidAndRemove();
            }

            try
            {
                if (payload.Length is 0 or > MaximumPayloadBytes
                    || !string.Equals(Convert.ToBase64String(payload), encoded, StringComparison.Ordinal)
                    || !Sr5CareerWizardDesktopSession.TryDeserializeCheckpoint(
                        payload,
                        out Sr5CareerWizardCheckpoint? checkpoint)
                    || checkpoint is null)
                {
                    return InvalidAndRemove();
                }
                return new(Sr5CareerWizardPhoneCheckpointReadStatus.Ready, checkpoint);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(payload);
            }
        }
    }

    public bool TryWrite(Sr5CareerWizardDesktopSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        byte[] payload;
        try
        {
            payload = Sr5CareerWizardDesktopSession.SerializeCheckpoint(session.CreateCheckpoint());
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        try
        {
            if (payload.Length is 0 or > MaximumPayloadBytes)
                return false;
            string encoded = Convert.ToBase64String(payload);
            lock (_sync)
            {
                try
                {
                    _backend.Write(encoded);
                    string readBack = _backend.Read();
                    if (string.Equals(encoded, readBack, StringComparison.Ordinal))
                        return true;
                    TryRemove();
                    return false;
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    TryRemove();
                    return false;
                }
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    public void Clear()
    {
        lock (_sync)
            TryRemove();
    }

    private Sr5CareerWizardPhoneCheckpointRead InvalidAndRemove()
    {
        TryRemove();
        return new(Sr5CareerWizardPhoneCheckpointReadStatus.Invalid, null);
    }

    private void TryRemove()
    {
        try
        {
            _backend.Remove();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A failed delete never turns an invalid checkpoint into an accepted checkpoint.
        }
    }
}
