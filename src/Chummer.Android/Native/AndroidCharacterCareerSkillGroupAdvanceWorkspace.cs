using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Android.Native;

public interface IAndroidCareerSkillGroupSettingsCatalog
{
    string ReadCatalogJson();
}

/// <summary>
/// Android's durable SR5 Career skill-group authority. The adapter projects from the
/// saved runner document and commits the runner mutation plus its immutable Core receipt
/// through the workspace store's single replacement/checkpoint CAS. Presentation XML
/// mutation is deliberately not a fallback for this authority boundary.
/// </summary>
public sealed class AndroidCharacterCareerSkillGroupAdvanceWorkspace :
    ICharacterCareerSkillGroupAdvanceWorkspace
{
    private const string LedgerElementName = "androidcareerskillgroupadvanceledger";
    private const string LedgerVersion = "1";
    private const int DefaultMaximumActiveSkillRating = 12;
    private const int MaximumLedgerEntries = 4096;
    private const int MaximumLedgerJsonLength = 1_048_576;
    private static readonly ConcurrentDictionary<string, object> WorkspaceGates =
        new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions LedgerJsonOptions = new()
    {
        PropertyNameCaseInsensitive = false
    };

    private readonly IWorkspaceStore _store;
    private readonly ICharacterSourceDataResolver _sourceData;
    private readonly IAndroidCareerSkillGroupSettingsCatalog _settingsCatalog;

    public AndroidCharacterCareerSkillGroupAdvanceWorkspace(
        IWorkspaceStore store,
        ICharacterSourceDataResolver sourceData,
        IAndroidCareerSkillGroupSettingsCatalog settingsCatalog)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _sourceData = sourceData ?? throw new ArgumentNullException(nameof(sourceData));
        _settingsCatalog = settingsCatalog
            ?? throw new ArgumentNullException(nameof(settingsCatalog));
    }

    public CharacterCareerSkillGroupWorkspaceReadResult Read(
        CharacterWorkspaceId workspaceId,
        CharacterCareerSkillGroupIdentity identity)
    {
        if (!IsValidWorkspaceId(workspaceId) || !IsValidIdentity(identity))
        {
            return new(
                CharacterCareerSkillGroupWorkspaceOutcome.Corrupt,
                Error: "invalid_workspace_or_skill_group_identity");
        }

        lock (Gate(workspaceId))
        {
            WorkspaceStoreReadResult read = ReadStore(workspaceId);
            if (!TryRequireSavedSr5Document(read, out WorkspaceStoredDocument saved, out var failure))
            {
                return new(failure.Outcome, failure.Revision, Error: failure.Error);
            }

            try
            {
                XDocument document = ParseDocument(saved.Document.Content);
                _ = ReadLedger(document, workspaceId);
                CharacterCareerSkillGroupAdvanceInput input = ProjectInput(document, identity);
                return new(
                    CharacterCareerSkillGroupWorkspaceOutcome.Available,
                    saved.ContentRevision,
                    input);
            }
            catch (WorkspaceTargetMissingException error)
            {
                return new(
                    CharacterCareerSkillGroupWorkspaceOutcome.NotFound,
                    saved.ContentRevision,
                    Error: error.Message);
            }
            catch (WorkspaceAuthorityUnavailableException error)
            {
                return new(
                    CharacterCareerSkillGroupWorkspaceOutcome.Unavailable,
                    saved.ContentRevision,
                    Error: error.Message);
            }
            catch (Exception error) when (IsProjectionFailure(error))
            {
                return new(
                    CharacterCareerSkillGroupWorkspaceOutcome.Corrupt,
                    saved.ContentRevision,
                    Error: "skill_group_projection_corrupt");
            }
        }
    }

    public CharacterCareerSkillGroupWorkspaceLookupResult Lookup(
        CharacterWorkspaceId workspaceId,
        Guid transactionId,
        string commandDigest)
    {
        if (!IsValidWorkspaceId(workspaceId)
            || transactionId == Guid.Empty
            || !CharacterCareerSkillGroupAdvanceServiceIntegrity.IsCanonicalDigest(commandDigest))
        {
            return new(
                CharacterCareerSkillGroupWorkspaceOutcome.Corrupt,
                Error: "invalid_replay_lookup");
        }

        lock (Gate(workspaceId))
        {
            return LookupUnderGate(workspaceId, transactionId, commandDigest);
        }
    }

    public CharacterCareerSkillGroupWorkspaceCommitResult Commit(
        CharacterCareerSkillGroupWorkspaceCommitRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsValidCommitRequest(request))
        {
            return new(
                CharacterCareerSkillGroupWorkspaceOutcome.Corrupt,
                Error: "invalid_atomic_commit_request");
        }

        lock (Gate(request.WorkspaceId))
        {
            CharacterCareerSkillGroupWorkspaceLookupResult prior = LookupUnderGate(
                request.WorkspaceId,
                request.Plan.TransactionId,
                request.CommandDigest);
            if (prior.Outcome == CharacterCareerSkillGroupWorkspaceOutcome.Replayed)
            {
                return ReplayCommit(prior);
            }
            if (prior.Outcome != CharacterCareerSkillGroupWorkspaceOutcome.NotFound)
            {
                return CommitFromLookup(prior);
            }

            WorkspaceStoreReadResult read = ReadStore(request.WorkspaceId);
            if (!TryRequireSavedSr5Document(read, out WorkspaceStoredDocument saved, out var failure))
            {
                return new(failure.Outcome, failure.Revision, Error: failure.Error);
            }
            if (saved.ContentRevision != request.ExpectedWorkspaceRevision)
            {
                return new(
                    CharacterCareerSkillGroupWorkspaceOutcome.Conflict,
                    saved.ContentRevision,
                    Error: "stale_workspace_revision");
            }

            try
            {
                XDocument document = ParseDocument(saved.Document.Content);
                CharacterCareerSkillGroupAdvanceInput input = ProjectInput(
                    document,
                    request.Plan.Identity);
                if (!CharacterCareerSkillGroupAdvanceRules.TryCreateQuote(
                        input,
                        out CharacterCareerSkillGroupAdvanceQuote current)
                    || !CanonicalEquals(current, request.ReviewedQuote)
                    || !CharacterCareerSkillGroupAdvanceRules.TryPlanAdvance(
                        current,
                        request.ReviewedQuote.LogicalRevision,
                        request.ReviewedQuote.SourceRevision,
                        request.ReviewedQuote.RuleDigest,
                        confirmed: true,
                        transactionIdAlreadyExists: false,
                        request.Plan.TransactionId,
                        request.Plan.ExpenseDateLocal,
                        out CharacterCareerSkillGroupAdvancePlan exactPlan)
                    || !CanonicalEquals(exactPlan, request.Plan))
                {
                    return new(
                        CharacterCareerSkillGroupWorkspaceOutcome.Conflict,
                        saved.ContentRevision,
                        Error: "stale_or_forged_skill_group_plan");
                }

                ApplyPlan(document, exactPlan);
                CharacterCareerSkillGroupAdvanceInput postInput = ProjectInput(
                    document,
                    exactPlan.Identity);
                if (!CharacterCareerSkillGroupAdvanceRules.TryCreateQuote(
                        postInput,
                        out CharacterCareerSkillGroupAdvanceQuote postState))
                {
                    return new(
                        CharacterCareerSkillGroupWorkspaceOutcome.Corrupt,
                        saved.ContentRevision,
                        Error: "post_state_projection_invalid");
                }

                CharacterCareerSkillGroupExpenseObservation expense = ObserveExpense(
                    document,
                    exactPlan.ExpenseId);
                if (!CharacterCareerSkillGroupAdvanceRules.TryCreateReceipt(
                        exactPlan.TransactionId,
                        current,
                        exactPlan,
                        postState,
                        expense,
                        out CharacterCareerSkillGroupAdvanceReceipt receipt))
                {
                    return new(
                        CharacterCareerSkillGroupWorkspaceOutcome.Corrupt,
                        saved.ContentRevision,
                        Error: "receipt_creation_failed");
                }

                long committedRevision = checked(saved.ContentRevision + 1);
                PersistLedgerEntry(
                    document,
                    request.WorkspaceId,
                    saved.ContentRevision,
                    committedRevision,
                    request.CommandDigest,
                    current,
                    receipt);
                string payload = Serialize(document);
                WorkspaceDocument replacement = new(
                    saved.Document.State with { Payload = payload },
                    saved.Document.Format);

                WorkspaceStoreMutationResult committed;
                try
                {
                    committed = _store.ReplaceWorkspaceDocumentAndCheckpoint(
                        request.WorkspaceId,
                        request.ExpectedWorkspaceRevision,
                        replacement);
                }
                catch (Exception error) when (IsStoreFailure(error))
                {
                    return RecoverUnknownCommit(request, error.Message);
                }

                if (committed.Success
                    && committed.Entry is { } entry
                    && entry.ContentRevision == committedRevision
                    && entry.SavedRevision == committedRevision)
                {
                    return new(
                        CharacterCareerSkillGroupWorkspaceOutcome.Applied,
                        committedRevision,
                        request.CommandDigest,
                        current,
                        receipt);
                }

                CharacterCareerSkillGroupWorkspaceCommitResult recovered =
                    RecoverUnknownCommit(request, committed.Error);
                if (recovered.Outcome == CharacterCareerSkillGroupWorkspaceOutcome.Replayed)
                {
                    return recovered;
                }
                return committed.Outcome switch
                {
                    WorkspaceOperationOutcome.Missing => new(
                        CharacterCareerSkillGroupWorkspaceOutcome.Missing,
                        Error: committed.Error ?? "workspace_missing"),
                    WorkspaceOperationOutcome.Conflict => new(
                        CharacterCareerSkillGroupWorkspaceOutcome.Conflict,
                        recovered.CurrentWorkspaceRevision,
                        Error: committed.Error ?? "workspace_revision_conflict"),
                    WorkspaceOperationOutcome.Corrupt => new(
                        CharacterCareerSkillGroupWorkspaceOutcome.Corrupt,
                        recovered.CurrentWorkspaceRevision,
                        Error: committed.Error ?? "workspace_corrupt"),
                    _ => recovered
                };
            }
            catch (WorkspaceTargetMissingException error)
            {
                return new(
                    CharacterCareerSkillGroupWorkspaceOutcome.Missing,
                    saved.ContentRevision,
                    Error: error.Message);
            }
            catch (WorkspaceAuthorityUnavailableException error)
            {
                return new(
                    CharacterCareerSkillGroupWorkspaceOutcome.Unavailable,
                    saved.ContentRevision,
                    Error: error.Message);
            }
            catch (OverflowException)
            {
                return new(
                    CharacterCareerSkillGroupWorkspaceOutcome.Corrupt,
                    saved.ContentRevision,
                    Error: "workspace_revision_exhausted");
            }
            catch (Exception error) when (IsProjectionFailure(error))
            {
                return new(
                    CharacterCareerSkillGroupWorkspaceOutcome.Corrupt,
                    saved.ContentRevision,
                    Error: "atomic_skill_group_projection_corrupt");
            }
        }
    }

    private CharacterCareerSkillGroupWorkspaceCommitResult RecoverUnknownCommit(
        CharacterCareerSkillGroupWorkspaceCommitRequest request,
        string? error)
    {
        CharacterCareerSkillGroupWorkspaceLookupResult lookup = LookupUnderGate(
            request.WorkspaceId,
            request.Plan.TransactionId,
            request.CommandDigest);
        if (lookup.Outcome == CharacterCareerSkillGroupWorkspaceOutcome.Replayed)
        {
            return ReplayCommit(lookup);
        }
        if (lookup.Outcome == CharacterCareerSkillGroupWorkspaceOutcome.IdempotencyConflict
            || lookup.Outcome == CharacterCareerSkillGroupWorkspaceOutcome.Corrupt
            || lookup.Outcome == CharacterCareerSkillGroupWorkspaceOutcome.Missing)
        {
            return CommitFromLookup(lookup);
        }
        return new(
            CharacterCareerSkillGroupWorkspaceOutcome.Unavailable,
            lookup.CurrentWorkspaceRevision,
            Error: error ?? lookup.Error ?? "atomic_commit_outcome_unknown");
    }

    private CharacterCareerSkillGroupWorkspaceLookupResult LookupUnderGate(
        CharacterWorkspaceId workspaceId,
        Guid transactionId,
        string commandDigest)
    {
        WorkspaceStoreReadResult read = ReadStore(workspaceId);
        if (!TryRequireSavedSr5Document(read, out WorkspaceStoredDocument saved, out var failure))
        {
            return new(failure.Outcome, failure.Revision, Error: failure.Error);
        }

        try
        {
            XDocument document = ParseDocument(saved.Document.Content);
            IReadOnlyList<PersistedLedgerEntry> entries = ReadLedger(document, workspaceId);
            PersistedLedgerEntry? match = entries.SingleOrDefault(
                candidate => candidate.TransactionId == transactionId);
            if (match is null)
            {
                return new(
                    CharacterCareerSkillGroupWorkspaceOutcome.NotFound,
                    saved.ContentRevision);
            }
            if (!FixedEquals(match.CommandDigest, commandDigest))
            {
                return new(
                    CharacterCareerSkillGroupWorkspaceOutcome.IdempotencyConflict,
                    saved.ContentRevision,
                    match.CommandDigest,
                    Error: "transaction_id_claimed_by_different_command");
            }
            return new(
                CharacterCareerSkillGroupWorkspaceOutcome.Replayed,
                saved.ContentRevision,
                match.CommandDigest,
                match.ReviewedQuote,
                match.Receipt);
        }
        catch (Exception error) when (IsProjectionFailure(error))
        {
            return new(
                CharacterCareerSkillGroupWorkspaceOutcome.Corrupt,
                saved.ContentRevision,
                Error: "skill_group_receipt_ledger_corrupt");
        }
    }

    private CharacterCareerSkillGroupAdvanceInput ProjectInput(
        XDocument document,
        CharacterCareerSkillGroupIdentity identity)
    {
        XElement root = RequireCharacterRoot(document);
        if (!ReadRequiredBool(root, "created"))
        {
            throw new WorkspaceAuthorityUnavailableException(
                "skill_group_advance_requires_career_runner");
        }

        string rawXml = Serialize(document);
        ICharacterSourceDataContext sourceContext = _sourceData.TryCreateContext(rawXml)
            ?? throw new WorkspaceAuthorityUnavailableException(
                "exact_skill_source_profile_unavailable");
        XElement settings = ResolveExactSettings(root, _settingsCatalog.ReadCatalogJson());
        CharacterCareerSkillGroupAdvanceSettings rules = new(
            ReadRequiredNonNegativeInt(settings, "karmacost", "karmanewskillgroup"),
            ReadRequiredNonNegativeInt(settings, "karmacost", "karmaimproveskillgroup"));
        int maximumRating = ReadOptionalNonNegativeInt(
            settings,
            "maxskillrating",
            DefaultMaximumActiveSkillRating);
        bool usePointsOnBrokenGroups = ReadOptionalBool(
            settings,
            "usepointsonbrokengroups",
            false);
        int availableKarma = ReadRequiredNonNegativeInt(root, "karma");
        XElement newSkills = RequireSingle(root, "newskills");
        XElement skillContainer = RequireSingle(newSkills, "skills");
        XElement groupContainer = RequireSingle(newSkills, "groups");
        XElement[] improvementContainers = root.Elements("improvements").Take(2).ToArray();
        if (improvementContainers.Length > 1)
        {
            throw new InvalidOperationException("duplicate_improvements_container");
        }
        XElement? improvements = improvementContainers.SingleOrDefault();
        string rawRuleState = settings.ToString(SaveOptions.DisableFormatting)
            + "\n"
            + (improvements?.ToString(SaveOptions.DisableFormatting) ?? "<improvements />");

        XElement[] groups = groupContainer.Elements("group").ToArray();
        HashSet<Guid> groupIds = [];
        HashSet<string> groupNames = new(StringComparer.Ordinal);
        foreach (XElement group in groups)
        {
            if (!groupIds.Add(ReadRequiredGuid(group, "id"))
                || !groupNames.Add(ReadRequiredText(group, "name")))
            {
                throw new InvalidOperationException("duplicate_skill_group_identity");
            }
        }
        XElement[] targetGroups = groups
            .Where(group => ReadRequiredGuid(group, "id") == identity.InternalId)
            .Take(2)
            .ToArray();
        if (targetGroups.Length == 0)
        {
            throw new WorkspaceTargetMissingException("skill_group_missing");
        }
        if (targetGroups.Length != 1)
        {
            throw new InvalidOperationException("ambiguous_skill_group_identity");
        }

        XElement[] skills = skillContainer.Elements("skill").ToArray();
        HashSet<Guid> skillIds = [];
        foreach (XElement skill in skills)
        {
            if (!skillIds.Add(ReadRequiredGuid(skill, "guid")))
            {
                throw new InvalidOperationException("duplicate_active_skill_identity");
            }
        }

        XElement target = targetGroups[0];
        Guid groupId = ReadRequiredGuid(target, "id");
        string groupName = ReadRequiredText(target, "name");
        int groupBase = ReadRequiredNonNegativeInt(target, "base");
        int groupKarma = ReadRequiredNonNegativeInt(target, "karma");
        bool broken = ReadRequiredBool(target, "isbroken");
        List<CharacterCareerSkillGroupMember> members = [];
        List<string> rawSources = [];
        foreach (XElement skill in skills)
        {
            if (ReadRequiredBool(skill, "isknowledge"))
            {
                throw new WorkspaceAuthorityUnavailableException(
                    "active_skill_projection_contains_knowledge_skill");
            }
            Guid skillId = ReadRequiredGuid(skill, "guid");
            Guid sourceId = ReadRequiredGuid(skill, "suid");
            if (!sourceContext.TryResolveActiveSkillSource(
                    sourceId.ToString("D"),
                    out CharacterActiveSkillSource source)
                || !Guid.TryParse(source.SourceSkillId, out Guid resolvedSourceId)
                || resolvedSourceId != sourceId)
            {
                throw new WorkspaceAuthorityUnavailableException(
                    "exact_active_skill_source_unavailable");
            }
            if (!string.Equals(source.SkillGroup, groupName, StringComparison.Ordinal))
            {
                continue;
            }
            string category = ReadRequiredText(skill, "skillcategory");
            if (!string.Equals(category, source.SkillCategory, StringComparison.Ordinal)
                || source.IsExotic
                || source.RequiresGroundMovement
                || source.RequiresSwimMovement
                || source.RequiresFlyMovement
                || HasUnsupportedRatingAuthority(improvements, source.Name, groupName))
            {
                throw new WorkspaceAuthorityUnavailableException(
                    "skill_group_member_authority_unavailable");
            }

            int skillBase = ReadRequiredNonNegativeInt(skill, "base");
            int skillKarma = ReadRequiredNonNegativeInt(skill, "karma");
            int effectiveBase = groupBase > 0 && !usePointsOnBrokenGroups
                ? groupBase
                : checked(groupBase + skillBase);
            int effectiveKarma = checked(groupKarma + skillKarma);
            int totalBaseRating = checked(
                Math.Min(effectiveBase, maximumRating)
                + Math.Min(effectiveKarma, maximumRating));
            if (totalBaseRating > maximumRating)
            {
                throw new WorkspaceAuthorityUnavailableException(
                    "skill_group_member_rating_unavailable");
            }
            members.Add(new CharacterCareerSkillGroupMember(
                skillId,
                totalBaseRating,
                !IsSkillDisabled(improvements, source.Name, category),
                category));
            rawSources.Add(source.RawSourceXml);
        }
        if (members.Count == 0)
        {
            throw new WorkspaceAuthorityUnavailableException(
                "skill_group_has_no_exact_members");
        }

        string[] categories = members
            .Select(member => member.SkillCategory)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (!TryResolveCostModifiers(
                improvements,
                groupName,
                categories,
                out IReadOnlyList<CharacterCareerSkillGroupKarmaModifier> modifiers))
        {
            throw new WorkspaceAuthorityUnavailableException(
                "skill_group_modifier_authority_unavailable");
        }

        return new CharacterCareerSkillGroupAdvanceInput(
            new CharacterCareerSkillGroupIdentity(groupId),
            Created: true,
            CharacterCareerSkillGroupAdvanceRules.RulesetId,
            TargetOwnedByCharacter: true,
            MemberProjectionIsExact: true,
            groupName,
            groupBase,
            groupKarma,
            maximumRating,
            availableKarma,
            IsGroupDisabled(improvements, groupName, categories),
            broken,
            rules,
            members,
            modifiers,
            string.Join("\n", rawSources.OrderBy(value => value, StringComparer.Ordinal)),
            rawRuleState);
    }

    private static void ApplyPlan(
        XDocument document,
        CharacterCareerSkillGroupAdvancePlan plan)
    {
        XElement root = RequireCharacterRoot(document);
        XElement groups = RequireSingle(RequireSingle(root, "newskills"), "groups");
        XElement[] matches = groups.Elements("group")
            .Where(group => ReadRequiredGuid(group, "id") == plan.Identity.InternalId)
            .Take(2)
            .ToArray();
        if (matches.Length != 1)
        {
            throw new WorkspaceTargetMissingException("skill_group_missing_during_commit");
        }
        SetRequiredValue(matches[0], "karma", plan.SavedGroupKarmaPoints);
        SetRequiredValue(root, "karma", plan.SavedCharacterKarma);
        AddExpense(root, plan);
    }

    private static void AddExpense(
        XElement root,
        CharacterCareerSkillGroupAdvancePlan plan)
    {
        XElement[] containers = root.Elements("expenses").Take(2).ToArray();
        if (containers.Length > 1)
        {
            throw new InvalidOperationException("duplicate_expenses_container");
        }
        XElement expenses = containers.SingleOrDefault() ?? new XElement("expenses");
        if (expenses.Parent is null)
        {
            root.Add(expenses);
        }
        if (expenses.Elements("expense").Any(
                expense => ReadRequiredGuid(expense, "guid") == plan.ExpenseId))
        {
            throw new InvalidOperationException("duplicate_expense_identity");
        }

        expenses.Add(new XElement(
            "expense",
            new XElement("guid", plan.ExpenseId.ToString("D")),
            new XElement("date", plan.ExpenseDateLocal.ToString("s", CultureInfo.InvariantCulture)),
            new XElement("amount", plan.ExpenseAmount.ToString(CultureInfo.InvariantCulture)),
            new XElement("reason", plan.ExpenseReason),
            new XElement("type", "Karma"),
            new XElement("refund", "False"),
            new XElement("forcecareervisible", "True"),
            new XElement(
                "undo",
                new XElement("karmatype", plan.KarmaUndoType),
                new XElement("nuyentype", plan.NuyenUndoType),
                new XElement("objectid", plan.UndoObjectId),
                new XElement("qty", plan.UndoQuantity.ToString(CultureInfo.InvariantCulture)),
                new XElement("extra", plan.UndoExtra))));
    }

    private static CharacterCareerSkillGroupExpenseObservation ObserveExpense(
        XDocument document,
        Guid expenseId)
    {
        XElement root = RequireCharacterRoot(document);
        XElement[] containers = root.Elements("expenses").Take(2).ToArray();
        if (containers.Length != 1)
        {
            throw new InvalidOperationException("missing_or_duplicate_expenses_container");
        }
        XElement[] matches = containers[0].Elements("expense")
            .Where(expense => ReadRequiredGuid(expense, "guid") == expenseId)
            .Take(2)
            .ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidOperationException("expense_observation_not_unique");
        }
        XElement expense = matches[0];
        XElement undo = RequireSingle(expense, "undo");
        return new CharacterCareerSkillGroupExpenseObservation(
            MatchingEntryCount: 1,
            expenseId,
            ReadRequiredDate(expense, "date"),
            ReadRequiredInt(expense, "amount"),
            ReadRequiredText(expense, "reason"),
            ReadRequiredText(expense, "type"),
            ReadRequiredBool(expense, "refund"),
            ReadRequiredBool(expense, "forcecareervisible"),
            ReadRequiredText(undo, "karmatype"),
            ReadRequiredText(undo, "nuyentype"),
            ReadRequiredText(undo, "objectid"),
            ReadRequiredDecimal(undo, "qty"),
            ReadOptionalText(undo, "extra", string.Empty));
    }

    private static void PersistLedgerEntry(
        XDocument document,
        CharacterWorkspaceId workspaceId,
        long expectedRevision,
        long committedRevision,
        string commandDigest,
        CharacterCareerSkillGroupAdvanceQuote reviewedQuote,
        CharacterCareerSkillGroupAdvanceReceipt receipt)
    {
        XElement root = RequireCharacterRoot(document);
        IReadOnlyList<PersistedLedgerEntry> existing = ReadLedger(document, workspaceId);
        if (existing.Any(entry => entry.TransactionId == receipt.TransactionId))
        {
            throw new InvalidOperationException("transaction_already_claimed");
        }
        if (!CharacterCareerSkillGroupAdvanceServiceIntegrity.TryComputeBindingDigest(
                workspaceId,
                expectedRevision,
                reviewedQuote,
                out string bindingDigest))
        {
            throw new InvalidOperationException("binding_digest_failed");
        }
        var unsigned = new CharacterCareerSkillGroupAdvanceResult(
            CharacterCareerSkillGroupAdvanceServiceSchemas.ResultV1,
            CharacterCareerSkillGroupAdvanceServiceOutcome.Applied,
            workspaceId,
            expectedRevision,
            committedRevision,
            reviewedQuote.Identity,
            receipt.TransactionId,
            commandDigest,
            reviewedQuote,
            receipt,
            [],
            string.Empty);
        if (!CharacterCareerSkillGroupAdvanceServiceIntegrity.TryComputeResultDigest(
                unsigned,
                out string resultDigest))
        {
            throw new InvalidOperationException("result_digest_failed");
        }

        XElement[] ledgers = root.Elements(LedgerElementName).Take(2).ToArray();
        XElement ledger = ledgers.Length switch
        {
            0 => new XElement(LedgerElementName, new XAttribute("version", LedgerVersion)),
            1 => ledgers[0],
            _ => throw new InvalidOperationException("duplicate_skill_group_receipt_ledger")
        };
        if (ledger.Parent is null)
        {
            root.Add(ledger);
        }
        RequireLedgerVersion(ledger);
        ledger.Add(new XElement(
            "entry",
            new XElement("transactionid", receipt.TransactionId.ToString("D")),
            new XElement("expectedworkspacerevision", expectedRevision.ToString(CultureInfo.InvariantCulture)),
            new XElement("committedworkspacerevision", committedRevision.ToString(CultureInfo.InvariantCulture)),
            new XElement("commanddigest", commandDigest),
            new XElement("bindingdigest", bindingDigest),
            new XElement("appliedresultdigest", resultDigest),
            new XElement("reviewedquotejson", JsonSerializer.Serialize(reviewedQuote, LedgerJsonOptions)),
            new XElement("receiptjson", JsonSerializer.Serialize(receipt, LedgerJsonOptions))));
    }

    private static IReadOnlyList<PersistedLedgerEntry> ReadLedger(
        XDocument document,
        CharacterWorkspaceId workspaceId)
    {
        XElement root = RequireCharacterRoot(document);
        XElement[] ledgers = root.Elements(LedgerElementName).Take(2).ToArray();
        if (ledgers.Length == 0)
        {
            return [];
        }
        if (ledgers.Length != 1)
        {
            throw new InvalidOperationException("duplicate_skill_group_receipt_ledger");
        }
        XElement ledger = ledgers[0];
        RequireLedgerVersion(ledger);
        XElement[] rawEntries = ledger.Elements("entry").ToArray();
        if (rawEntries.Length > MaximumLedgerEntries)
        {
            throw new InvalidOperationException("skill_group_receipt_ledger_too_large");
        }

        List<PersistedLedgerEntry> result = [];
        HashSet<Guid> transactionIds = [];
        foreach (XElement raw in rawEntries)
        {
            RequireExactLedgerEntryShape(raw);
            Guid transactionId = ReadRequiredGuid(raw, "transactionid");
            long expectedRevision = ReadRequiredPositiveLong(raw, "expectedworkspacerevision");
            long committedRevision = ReadRequiredPositiveLong(raw, "committedworkspacerevision");
            string commandDigest = ReadRequiredDigest(raw, "commanddigest");
            string bindingDigest = ReadRequiredDigest(raw, "bindingdigest");
            string resultDigest = ReadRequiredDigest(raw, "appliedresultdigest");
            string quoteJson = ReadRequiredBoundedText(raw, "reviewedquotejson");
            string receiptJson = ReadRequiredBoundedText(raw, "receiptjson");
            CharacterCareerSkillGroupAdvanceQuote reviewed =
                JsonSerializer.Deserialize<CharacterCareerSkillGroupAdvanceQuote>(
                    quoteJson,
                    LedgerJsonOptions)
                ?? throw new InvalidOperationException("reviewed_quote_missing");
            CharacterCareerSkillGroupAdvanceReceipt receipt =
                JsonSerializer.Deserialize<CharacterCareerSkillGroupAdvanceReceipt>(
                    receiptJson,
                    LedgerJsonOptions)
                ?? throw new InvalidOperationException("receipt_missing");
            if (!transactionIds.Add(transactionId)
                || committedRevision != checked(expectedRevision + 1)
                || receipt.TransactionId != transactionId
                || reviewed.Identity != receipt.Identity
                || !CharacterCareerSkillGroupAdvanceRules.IsCoherent(reviewed)
                || !CharacterCareerSkillGroupAdvanceRules.IsCoherent(receipt)
                || !FixedEquals(receipt.LogicalRevisionBefore, reviewed.LogicalRevision)
                || !FixedEquals(receipt.SourceRevisionBefore, reviewed.SourceRevision)
                || !FixedEquals(receipt.RuleDigestBefore, reviewed.RuleDigest)
                || !CharacterCareerSkillGroupAdvanceServiceIntegrity.TryComputeBindingDigest(
                    workspaceId,
                    expectedRevision,
                    reviewed,
                    out string expectedBindingDigest)
                || !FixedEquals(expectedBindingDigest, bindingDigest))
            {
                throw new InvalidOperationException("skill_group_receipt_binding_invalid");
            }
            var unsigned = new CharacterCareerSkillGroupAdvanceResult(
                CharacterCareerSkillGroupAdvanceServiceSchemas.ResultV1,
                CharacterCareerSkillGroupAdvanceServiceOutcome.Applied,
                workspaceId,
                expectedRevision,
                committedRevision,
                reviewed.Identity,
                transactionId,
                commandDigest,
                reviewed,
                receipt,
                [],
                string.Empty);
            if (!CharacterCareerSkillGroupAdvanceServiceIntegrity.TryComputeResultDigest(
                    unsigned,
                    out string expectedResultDigest)
                || !FixedEquals(expectedResultDigest, resultDigest))
            {
                throw new InvalidOperationException("skill_group_result_digest_invalid");
            }
            result.Add(new PersistedLedgerEntry(
                transactionId,
                expectedRevision,
                committedRevision,
                commandDigest,
                bindingDigest,
                resultDigest,
                reviewed,
                receipt));
        }
        return result;
    }

    private static bool TryRequireSavedSr5Document(
        WorkspaceStoreReadResult read,
        out WorkspaceStoredDocument saved,
        out WorkspaceFailure failure)
    {
        saved = null!;
        failure = default;
        if (!read.Success || read.Value is not { } value)
        {
            failure = new WorkspaceFailure(
                read.Outcome switch
                {
                    WorkspaceOperationOutcome.Missing => CharacterCareerSkillGroupWorkspaceOutcome.Missing,
                    WorkspaceOperationOutcome.Corrupt => CharacterCareerSkillGroupWorkspaceOutcome.Corrupt,
                    _ => CharacterCareerSkillGroupWorkspaceOutcome.Unavailable
                },
                0,
                read.Error ?? "workspace_read_failed");
            return false;
        }
        if (value.ContentRevision <= 0
            || value.SavedRevision != value.ContentRevision
            || value.Document.Format != WorkspaceDocumentFormat.NativeXml
            || !string.Equals(
                value.Document.RulesetId,
                CharacterCareerSkillGroupAdvanceRules.RulesetId,
                StringComparison.Ordinal))
        {
            failure = new WorkspaceFailure(
                CharacterCareerSkillGroupWorkspaceOutcome.Unavailable,
                value.ContentRevision,
                "workspace_is_not_a_clean_saved_sr5_runner");
            return false;
        }
        saved = value;
        return true;
    }

    private WorkspaceStoreReadResult ReadStore(CharacterWorkspaceId workspaceId)
    {
        try
        {
            return _store.Get(workspaceId);
        }
        catch (Exception error) when (IsStoreFailure(error))
        {
            return new WorkspaceStoreReadResult(
                WorkspaceOperationOutcome.Unavailable,
                Error: "workspace_storage_unavailable");
        }
    }

    private static CharacterCareerSkillGroupWorkspaceCommitResult ReplayCommit(
        CharacterCareerSkillGroupWorkspaceLookupResult lookup)
        => new(
            CharacterCareerSkillGroupWorkspaceOutcome.Replayed,
            lookup.CurrentWorkspaceRevision,
            lookup.ExistingCommandDigest,
            lookup.ReviewedQuote,
            lookup.Receipt,
            lookup.Error);

    private static CharacterCareerSkillGroupWorkspaceCommitResult CommitFromLookup(
        CharacterCareerSkillGroupWorkspaceLookupResult lookup)
        => new(
            lookup.Outcome,
            lookup.CurrentWorkspaceRevision,
            lookup.ExistingCommandDigest,
            lookup.ReviewedQuote,
            lookup.Receipt,
            lookup.Error);

    private static bool IsValidCommitRequest(
        CharacterCareerSkillGroupWorkspaceCommitRequest request)
        => IsValidWorkspaceId(request.WorkspaceId)
            && request.ExpectedWorkspaceRevision > 0
            && request.ExpectedWorkspaceRevision < long.MaxValue
            && CharacterCareerSkillGroupAdvanceServiceIntegrity.IsCanonicalDigest(
                request.CommandDigest)
            && CharacterCareerSkillGroupAdvanceRules.IsCoherent(request.ReviewedQuote)
            && CharacterCareerSkillGroupAdvanceRules.IsCoherent(request.Plan)
            && request.ReviewedQuote.Identity == request.Plan.Identity
            && FixedEquals(
                request.ReviewedQuote.LogicalRevision,
                request.Plan.ExpectedLogicalRevision)
            && FixedEquals(
                request.ReviewedQuote.SourceRevision,
                request.Plan.ExpectedSourceRevision)
            && FixedEquals(
                request.ReviewedQuote.RuleDigest,
                request.Plan.ExpectedRuleDigest);

    private static object Gate(CharacterWorkspaceId workspaceId)
        => WorkspaceGates.GetOrAdd(workspaceId.Value, static _ => new object());

    private static bool IsValidWorkspaceId(CharacterWorkspaceId workspaceId)
        => !string.IsNullOrWhiteSpace(workspaceId.Value)
            && workspaceId.Value.Length
                <= CharacterCareerSkillGroupAdvanceServiceSchemas.MaximumWorkspaceIdLength
            && workspaceId.Value.All(character =>
                char.IsLetterOrDigit(character) || character is '-' or '_');

    private static bool IsValidIdentity(CharacterCareerSkillGroupIdentity identity)
        => identity is { InternalId: var id } && id != Guid.Empty;

    private static XDocument ParseDocument(string xml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);
        return XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
    }

    private static XElement RequireCharacterRoot(XDocument document)
        => document.Root is { } root && root.Name == XName.Get("character")
            ? root
            : throw new InvalidOperationException("invalid_character_root");

    private static XElement RequireSingle(XElement parent, string name)
    {
        XElement[] matches = parent.Elements(name).Take(2).ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw new InvalidOperationException($"missing_or_duplicate_{name}");
    }

    private static XElement ResolveExactSettings(XElement root, string catalogJson)
    {
        if (string.IsNullOrWhiteSpace(catalogJson))
        {
            throw new WorkspaceAuthorityUnavailableException(
                "character_settings_catalog_unavailable");
        }
        string settingsId = ReadRequiredText(root, "settings");
        using JsonDocument catalog = JsonDocument.Parse(catalogJson);
        JsonElement catalogRoot = catalog.RootElement;
        if (catalogRoot.ValueKind != JsonValueKind.Object
            || !TryGetUniqueProperty(catalogRoot, "Profiles", out JsonElement profiles)
            || profiles.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("character_settings_catalog_corrupt");
        }

        string? settingsXml = null;
        int matches = 0;
        foreach (JsonElement profile in profiles.EnumerateArray())
        {
            if (profile.ValueKind != JsonValueKind.Object
                || !TryGetUniqueProperty(profile, "Id", out JsonElement id)
                || id.ValueKind != JsonValueKind.String
                || !TryGetUniqueProperty(profile, "Xml", out JsonElement xml)
                || xml.ValueKind != JsonValueKind.String)
            {
                throw new InvalidOperationException("character_settings_profile_corrupt");
            }
            if (string.Equals(id.GetString(), settingsId, StringComparison.Ordinal))
            {
                matches++;
                settingsXml = xml.GetString();
            }
        }
        if (matches == 0)
        {
            throw new WorkspaceAuthorityUnavailableException(
                "exact_character_settings_profile_unavailable");
        }
        if (matches != 1 || string.IsNullOrWhiteSpace(settingsXml))
        {
            throw new InvalidOperationException("character_settings_profile_ambiguous");
        }
        XElement settings = XElement.Parse(settingsXml, LoadOptions.PreserveWhitespace);
        return settings.Name == XName.Get("settings")
            ? settings
            : throw new InvalidOperationException("character_settings_root_invalid");
    }

    private static bool TryGetUniqueProperty(
        JsonElement owner,
        string name,
        out JsonElement value)
    {
        value = default;
        int count = 0;
        foreach (JsonProperty property in owner.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                count++;
                value = property.Value;
            }
        }
        return count == 1;
    }

    private static bool HasUnsupportedRatingAuthority(
        XElement? improvements,
        string skillName,
        string groupName)
        => (improvements?.Elements("improvement") ?? [])
            .Any(improvement => IsEnabledInCareer(improvement)
                && ReadOptionalText(improvement, "improvementttype", string.Empty) is
                    "Skill" or "SkillBase" or "SkillLevel" or "SkillGroup" or "SkillGroupLevel"
                && (TargetMatches(
                        ReadOptionalText(improvement, "improvedname", string.Empty),
                        skillName)
                    || TargetMatches(
                        ReadOptionalText(improvement, "improvedname", string.Empty),
                        groupName)));

    private static bool IsSkillDisabled(
        XElement? improvements,
        string skillName,
        string category)
        => (improvements?.Elements("improvement") ?? [])
            .Any(improvement => IsEnabledInCareer(improvement)
                && ((ReadOptionalText(improvement, "improvementttype", string.Empty) == "SkillDisable"
                        && TargetMatches(
                            ReadOptionalText(improvement, "improvedname", string.Empty),
                            skillName))
                    || (ReadOptionalText(improvement, "improvementttype", string.Empty) == "SkillCategoryDisable"
                        && TargetMatches(
                            ReadOptionalText(improvement, "improvedname", string.Empty),
                            category))));

    private static bool IsGroupDisabled(
        XElement? improvements,
        string groupName,
        IReadOnlyCollection<string> categories)
        => (improvements?.Elements("improvement") ?? [])
            .Any(improvement => IsEnabledInCareer(improvement)
                && ((ReadOptionalText(improvement, "improvementttype", string.Empty) == "SkillGroupDisable"
                        && TargetMatches(
                            ReadOptionalText(improvement, "improvedname", string.Empty),
                            groupName))
                    || (ReadOptionalText(improvement, "improvementttype", string.Empty) == "SkillGroupCategoryDisable"
                        && categories.Contains(
                            ReadOptionalText(improvement, "improvedname", string.Empty),
                            StringComparer.Ordinal))));

    private static bool TryResolveCostModifiers(
        XElement? improvements,
        string groupName,
        IReadOnlyCollection<string> categories,
        out IReadOnlyList<CharacterCareerSkillGroupKarmaModifier> modifiers)
    {
        List<CharacterCareerSkillGroupKarmaModifier> result = [];
        int ordinal = 0;
        foreach (XElement improvement in improvements?.Elements("improvement") ?? [])
        {
            string type = ReadOptionalText(improvement, "improvementttype", string.Empty);
            if (!TryMapModifierKind(type, out CharacterCareerSkillGroupKarmaModifierKind kind)
                || !IsEnabledCareerImprovement(improvement))
            {
                ordinal++;
                continue;
            }
            string target = ReadOptionalText(improvement, "improvedname", string.Empty);
            bool targetMatches = kind is CharacterCareerSkillGroupKarmaModifierKind.SkillGroupCost
                    or CharacterCareerSkillGroupKarmaModifierKind.SkillGroupCostMultiplier
                ? TargetMatches(target, groupName)
                : string.IsNullOrEmpty(target)
                    || categories.Contains(target, StringComparer.Ordinal);
            if (!targetMatches)
            {
                ordinal++;
                continue;
            }
            if (!string.IsNullOrEmpty(ReadOptionalText(improvement, "unique", string.Empty))
                || !TryReadNonNegativeInt(improvement, "min", 0, out int minimum)
                || !TryReadNonNegativeInt(improvement, "max", 0, out int maximum)
                || maximum != 0 && maximum < minimum
                || !TryReadDecimal(improvement, "val", out decimal value))
            {
                modifiers = [];
                return false;
            }
            string raw = ordinal.ToString(CultureInfo.InvariantCulture)
                + "\0"
                + improvement.ToString(SaveOptions.DisableFormatting);
            string modifierIdentity = Convert.ToHexStringLower(
                SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
            result.Add(new CharacterCareerSkillGroupKarmaModifier(
                modifierIdentity,
                kind,
                target,
                minimum,
                maximum,
                value));
            ordinal++;
        }
        modifiers = result;
        return true;
    }

    private static bool TryMapModifierKind(
        string type,
        out CharacterCareerSkillGroupKarmaModifierKind kind)
    {
        switch (type)
        {
            case "SkillGroupKarmaCost":
                kind = CharacterCareerSkillGroupKarmaModifierKind.SkillGroupCost;
                return true;
            case "SkillGroupKarmaCostMultiplier":
                kind = CharacterCareerSkillGroupKarmaModifierKind.SkillGroupCostMultiplier;
                return true;
            case "SkillGroupCategoryKarmaCost":
                kind = CharacterCareerSkillGroupKarmaModifierKind.SkillGroupCategoryCost;
                return true;
            case "SkillGroupCategoryKarmaCostMultiplier":
                kind = CharacterCareerSkillGroupKarmaModifierKind.SkillGroupCategoryCostMultiplier;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    private static bool IsEnabledCareerImprovement(XElement improvement)
        => IsEnabledInCareer(improvement)
            && !ReadOptionalBool(improvement, "addtorating", false);

    private static bool IsEnabledInCareer(XElement improvement)
    {
        string condition = ReadOptionalText(improvement, "condition", string.Empty);
        return ReadOptionalBool(improvement, "enabled", true)
            && (string.IsNullOrEmpty(condition)
                || string.Equals(condition, "career", StringComparison.Ordinal));
    }

    private static bool TargetMatches(string target, string expected)
        => string.IsNullOrEmpty(target)
            || string.Equals(target, expected, StringComparison.Ordinal);

    private static void RequireLedgerVersion(XElement ledger)
    {
        XAttribute[] attributes = ledger.Attributes().ToArray();
        if (attributes.Length != 1
            || attributes[0].Name != XName.Get("version")
            || !string.Equals(attributes[0].Value, LedgerVersion, StringComparison.Ordinal)
            || ledger.Elements().Any(element => element.Name != XName.Get("entry")))
        {
            throw new InvalidOperationException("skill_group_receipt_ledger_schema_invalid");
        }
    }

    private static void RequireExactLedgerEntryShape(XElement entry)
    {
        string[] expected =
        [
            "transactionid",
            "expectedworkspacerevision",
            "committedworkspacerevision",
            "commanddigest",
            "bindingdigest",
            "appliedresultdigest",
            "reviewedquotejson",
            "receiptjson"
        ];
        XElement[] children = entry.Elements().ToArray();
        if (entry.HasAttributes
            || children.Length != expected.Length
            || expected.Any(name => children.Count(child => child.Name == XName.Get(name)) != 1)
            || children.Any(child => !expected.Contains(child.Name.LocalName, StringComparer.Ordinal)))
        {
            throw new InvalidOperationException("skill_group_receipt_entry_schema_invalid");
        }
    }

    private static Guid ReadRequiredGuid(XElement parent, string name)
    {
        XElement[] values = parent.Elements(name).Take(2).ToArray();
        if (values.Length != 1
            || !Guid.TryParse(values[0].Value.Trim(), out Guid value)
            || value == Guid.Empty)
        {
            throw new InvalidOperationException($"invalid_or_duplicate_{name}");
        }
        return value;
    }

    private static string ReadRequiredText(XElement parent, string name)
    {
        XElement[] values = parent.Elements(name).Take(2).ToArray();
        if (values.Length != 1 || string.IsNullOrWhiteSpace(values[0].Value))
        {
            throw new InvalidOperationException($"invalid_or_duplicate_{name}");
        }
        return values[0].Value.Trim();
    }

    private static string ReadRequiredBoundedText(XElement parent, string name)
    {
        string value = ReadRequiredText(parent, name);
        return value.Length <= MaximumLedgerJsonLength
            ? value
            : throw new InvalidOperationException($"{name}_too_large");
    }

    private static string ReadRequiredDigest(XElement parent, string name)
    {
        string value = ReadRequiredText(parent, name);
        return CharacterCareerSkillGroupAdvanceServiceIntegrity.IsCanonicalDigest(value)
            ? value
            : throw new InvalidOperationException($"invalid_{name}");
    }

    private static bool ReadRequiredBool(XElement parent, string name)
    {
        XElement[] values = parent.Elements(name).Take(2).ToArray();
        if (values.Length != 1 || !bool.TryParse(values[0].Value.Trim(), out bool value))
        {
            throw new InvalidOperationException($"invalid_or_duplicate_{name}");
        }
        return value;
    }

    private static int ReadRequiredNonNegativeInt(XElement parent, string name)
        => TryReadNonNegativeInt(parent, name, -1, out int value) && value >= 0
            ? value
            : throw new InvalidOperationException($"invalid_or_duplicate_{name}");

    private static int ReadRequiredNonNegativeInt(
        XElement parent,
        string containerName,
        string name)
        => ReadRequiredNonNegativeInt(RequireSingle(parent, containerName), name);

    private static int ReadOptionalNonNegativeInt(
        XElement parent,
        string name,
        int fallback)
        => TryReadNonNegativeInt(parent, name, fallback, out int value)
            ? value
            : throw new InvalidOperationException($"invalid_or_duplicate_{name}");

    private static int ReadRequiredInt(XElement parent, string name)
    {
        XElement[] values = parent.Elements(name).Take(2).ToArray();
        if (values.Length != 1
            || !int.TryParse(
                values[0].Value.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int value))
        {
            throw new InvalidOperationException($"invalid_or_duplicate_{name}");
        }
        return value;
    }

    private static long ReadRequiredPositiveLong(XElement parent, string name)
    {
        XElement[] values = parent.Elements(name).Take(2).ToArray();
        if (values.Length != 1
            || !long.TryParse(
                values[0].Value.Trim(),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out long value)
            || value <= 0)
        {
            throw new InvalidOperationException($"invalid_or_duplicate_{name}");
        }
        return value;
    }

    private static decimal ReadRequiredDecimal(XElement parent, string name)
    {
        XElement[] values = parent.Elements(name).Take(2).ToArray();
        if (values.Length != 1
            || !decimal.TryParse(
                values[0].Value.Trim(),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out decimal value))
        {
            throw new InvalidOperationException($"invalid_or_duplicate_{name}");
        }
        return value;
    }

    private static DateTime ReadRequiredDate(XElement parent, string name)
    {
        XElement[] values = parent.Elements(name).Take(2).ToArray();
        if (values.Length != 1
            || !DateTime.TryParse(
                values[0].Value.Trim(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out DateTime value))
        {
            throw new InvalidOperationException($"invalid_or_duplicate_{name}");
        }
        return DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
    }

    private static bool TryReadNonNegativeInt(
        XElement parent,
        string name,
        int fallback,
        out int value)
    {
        XElement[] values = parent.Elements(name).Take(2).ToArray();
        value = fallback;
        if (values.Length == 0
            || values.Length == 1 && string.IsNullOrWhiteSpace(values[0].Value))
        {
            return fallback >= 0;
        }
        return values.Length == 1
            && int.TryParse(
                values[0].Value.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value)
            && value >= 0;
    }

    private static bool TryReadDecimal(XElement parent, string name, out decimal value)
    {
        XElement[] values = parent.Elements(name).Take(2).ToArray();
        value = 0m;
        return values.Length == 1
            && decimal.TryParse(
                values[0].Value.Trim(),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out value);
    }

    private static bool ReadOptionalBool(XElement parent, string name, bool fallback)
    {
        XElement[] values = parent.Elements(name).Take(2).ToArray();
        if (values.Length == 0
            || values.Length == 1 && string.IsNullOrWhiteSpace(values[0].Value))
        {
            return fallback;
        }
        if (values.Length != 1 || !bool.TryParse(values[0].Value.Trim(), out bool value))
        {
            throw new InvalidOperationException($"invalid_or_duplicate_{name}");
        }
        return value;
    }

    private static string ReadOptionalText(
        XElement parent,
        string name,
        string fallback)
    {
        XElement[] values = parent.Elements(name).Take(2).ToArray();
        return values.Length switch
        {
            0 => fallback,
            1 => values[0].Value.Trim(),
            _ => throw new InvalidOperationException($"duplicate_{name}")
        };
    }

    private static void SetRequiredValue(XElement parent, string name, int value)
    {
        XElement element = RequireSingle(parent, name);
        element.Value = value.ToString(CultureInfo.InvariantCulture);
    }

    private static bool CanonicalEquals<T>(T left, T right)
    {
        byte[] leftBytes = JsonSerializer.SerializeToUtf8Bytes(left, LedgerJsonOptions);
        byte[] rightBytes = JsonSerializer.SerializeToUtf8Bytes(right, LedgerJsonOptions);
        return leftBytes.Length == rightBytes.Length
            && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static bool FixedEquals(string? left, string? right)
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

    private static string Serialize(XDocument document)
    {
        using StringWriter writer = new(CultureInfo.InvariantCulture);
        document.Save(writer, SaveOptions.DisableFormatting);
        return writer.ToString();
    }

    private static bool IsProjectionFailure(Exception error)
        => error is InvalidOperationException
            or ArgumentException
            or JsonException
            or XmlException
            or FormatException
            or OverflowException;

    private static bool IsStoreFailure(Exception error)
        => error is IOException
            or UnauthorizedAccessException
            or InvalidOperationException;

    private sealed record PersistedLedgerEntry(
        Guid TransactionId,
        long ExpectedWorkspaceRevision,
        long CommittedWorkspaceRevision,
        string CommandDigest,
        string BindingDigest,
        string AppliedResultDigest,
        CharacterCareerSkillGroupAdvanceQuote ReviewedQuote,
        CharacterCareerSkillGroupAdvanceReceipt Receipt);

    private readonly record struct WorkspaceFailure(
        CharacterCareerSkillGroupWorkspaceOutcome Outcome,
        long Revision,
        string Error);

    private sealed class WorkspaceTargetMissingException(string message) :
        Exception(message);

    private sealed class WorkspaceAuthorityUnavailableException(string message) :
        Exception(message);
}
