using System.Globalization;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

/// <summary>
/// Phone-local typed draft for one exact Creation Contacts snapshot. The draft never stores XML
/// names or persistence instructions. If any identity value changes, it emits all thirteen identity
/// values so Core can distinguish an intentional clear from an omitted edit.
/// </summary>
internal sealed class CreationContactsPhoneDraft
{
    private static readonly IReadOnlyList<string> s_IdentityFieldIds = Array.AsReadOnly(new[]
    {
        CharacterCreationContactFieldIds.Name,
        CharacterCreationContactFieldIds.Role,
        CharacterCreationContactFieldIds.Location,
        CharacterCreationContactFieldIds.Notes,
        CharacterCreationContactFieldIds.CustomName,
        CharacterCreationContactFieldIds.Metatype,
        CharacterCreationContactFieldIds.Gender,
        CharacterCreationContactFieldIds.Age,
        CharacterCreationContactFieldIds.ContactType,
        CharacterCreationContactFieldIds.PreferredPayment,
        CharacterCreationContactFieldIds.HobbiesVice,
        CharacterCreationContactFieldIds.PersonalLife,
        CharacterCreationContactFieldIds.GroupName
    });

    private CharacterCreationContactBinding? _binding;
    private string? _snapshotDigest;
    private Guid _contactId;
    private string? _contactDigest;
    private readonly Dictionary<string, string> _original = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    public bool Bind(
        CharacterCreationContactsInteractionState state,
        CharacterCreationContactProjection contact)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(contact);
        if (Matches(state, contact))
            return false;

        _binding = state.Binding;
        _snapshotDigest = state.SnapshotDigest;
        _contactId = contact.ContactId;
        _contactDigest = contact.ContactDigest;
        _original.Clear();
        _values.Clear();
        foreach (CharacterCreationContactFieldAuthority field in contact.Fields)
        {
            _original[field.FieldId] = field.SerializedValue;
            _values[field.FieldId] = field.SerializedValue;
        }
        return true;
    }

    public bool Matches(
        CharacterCreationContactsInteractionState state,
        CharacterCreationContactProjection contact)
        => _binding is not null
           && _contactId == contact.ContactId
           && CreationContactsPhoneAuthority.BindingEquals(_binding, state.Binding)
           && string.Equals(_snapshotDigest, state.SnapshotDigest, StringComparison.Ordinal)
           && string.Equals(_contactDigest, contact.ContactDigest, StringComparison.Ordinal)
           && CreationContactsPhoneAuthority.IsExactContact(contact)
           && _values.Count == CharacterCreationContactFieldIds.All.Count
           && _original.Count == CharacterCreationContactFieldIds.All.Count;

    public string Value(
        CharacterCreationContactsInteractionState state,
        CharacterCreationContactProjection contact,
        string fieldId)
        => Matches(state, contact) && _values.TryGetValue(fieldId, out string? value)
            ? value
            : string.Empty;

    public bool TrySetText(
        CharacterCreationContactsInteractionState state,
        CharacterCreationContactProjection contact,
        string fieldId,
        string? value)
    {
        if (!TryResolveEditableField(
                state,
                contact,
                fieldId,
                CharacterCreationContactValueKinds.Text,
                out CharacterCreationContactFieldAuthority? field))
        {
            return false;
        }
        value ??= string.Empty;
        if (field.Maximum is int maximum && value.Length > maximum
            || field.Minimum is int minimum && value.Length < minimum
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || value.Any(character => char.IsControl(character)
                                      && character is not '\r' and not '\n' and not '\t'))
        {
            return false;
        }
        _values[fieldId] = value;
        return true;
    }

    public bool TrySetInteger(
        CharacterCreationContactsInteractionState state,
        CharacterCreationContactProjection contact,
        string fieldId,
        int value)
    {
        if (!TryResolveEditableField(
                state,
                contact,
                fieldId,
                CharacterCreationContactValueKinds.Integer,
                out CharacterCreationContactFieldAuthority? field)
            || field.Minimum is int minimum && value < minimum
            || field.Maximum is int maximum && value > maximum)
        {
            return false;
        }
        string serialized = value.ToString(CultureInfo.InvariantCulture);
        if (!HasUniqueEnabledOption(field, serialized))
            return false;
        _values[fieldId] = serialized;
        return true;
    }

    public bool TrySetBoolean(
        CharacterCreationContactsInteractionState state,
        CharacterCreationContactProjection contact,
        string fieldId,
        bool value)
    {
        if (!TryResolveEditableField(
                state,
                contact,
                fieldId,
                CharacterCreationContactValueKinds.Boolean,
                out CharacterCreationContactFieldAuthority? field))
        {
            return false;
        }
        string serialized = value.ToString(CultureInfo.InvariantCulture);
        if (!HasUniqueEnabledOption(field, serialized))
            return false;
        _values[fieldId] = serialized;
        return true;
    }

    public bool HasChanges(
        CharacterCreationContactsInteractionState state,
        CharacterCreationContactProjection contact)
        => Matches(state, contact)
           && CharacterCreationContactFieldIds.All.Any(fieldId =>
               !string.Equals(_values[fieldId], _original[fieldId], StringComparison.Ordinal));

    public CharacterCreationContactEditInput? ToInput(
        CharacterCreationContactsInteractionState state,
        CharacterCreationContactProjection contact)
    {
        if (!HasChanges(state, contact))
            return null;

        bool IdentityChanged = s_IdentityFieldIds.Any(fieldId =>
            !string.Equals(_values[fieldId], _original[fieldId], StringComparison.Ordinal));
        CharacterCreationContactIdentity? identity = IdentityChanged
            ? new CharacterCreationContactIdentity(
                _values[CharacterCreationContactFieldIds.Name],
                _values[CharacterCreationContactFieldIds.Role],
                _values[CharacterCreationContactFieldIds.Location],
                _values[CharacterCreationContactFieldIds.Notes],
                _values[CharacterCreationContactFieldIds.CustomName],
                _values[CharacterCreationContactFieldIds.Metatype],
                _values[CharacterCreationContactFieldIds.Gender],
                _values[CharacterCreationContactFieldIds.Age],
                _values[CharacterCreationContactFieldIds.ContactType],
                _values[CharacterCreationContactFieldIds.PreferredPayment],
                _values[CharacterCreationContactFieldIds.HobbiesVice],
                _values[CharacterCreationContactFieldIds.PersonalLife],
                _values[CharacterCreationContactFieldIds.GroupName])
            : null;

        return new CharacterCreationContactEditInput(
            contact.ContactId,
            Identity: IdentityChanged ? identity : null,
            Connection: ChangedInt(CharacterCreationContactFieldIds.Connection),
            Loyalty: ChangedInt(CharacterCreationContactFieldIds.Loyalty),
            IsGroup: ChangedBool(CharacterCreationContactFieldIds.Group),
            Free: ChangedBool(CharacterCreationContactFieldIds.Free),
            Family: ChangedBool(CharacterCreationContactFieldIds.Family),
            Blackmail: ChangedBool(CharacterCreationContactFieldIds.Blackmail));
    }

    private int? ChangedInt(string fieldId)
        => string.Equals(_values[fieldId], _original[fieldId], StringComparison.Ordinal)
            ? null
            : int.Parse(_values[fieldId], NumberStyles.Integer, CultureInfo.InvariantCulture);

    private bool? ChangedBool(string fieldId)
        => string.Equals(_values[fieldId], _original[fieldId], StringComparison.Ordinal)
            ? null
            : bool.Parse(_values[fieldId]);

    private bool TryResolveEditableField(
        CharacterCreationContactsInteractionState state,
        CharacterCreationContactProjection contact,
        string fieldId,
        string valueKind,
        out CharacterCreationContactFieldAuthority field)
    {
        field = null!;
        if (!Matches(state, contact))
            return false;
        CharacterCreationContactFieldAuthority[] matches = contact.Fields
            .Where(candidate => string.Equals(candidate.FieldId, fieldId, StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        if (matches is not [{ IsEditable: true }]
            || !string.Equals(matches[0].ValueKind, valueKind, StringComparison.Ordinal))
        {
            return false;
        }
        field = matches[0];
        return true;
    }

    private static bool HasUniqueEnabledOption(
        CharacterCreationContactFieldAuthority field,
        string serialized)
        => field.LegalOptions.Count(option =>
               option.IsEnabled
               && option.Blockers.Count == 0
               && string.Equals(option.SerializedValue, serialized, StringComparison.Ordinal)) == 1;
}

internal static class CreationContactsPhoneAuthority
{
    public static bool IsBound(
        CharacterCreationContactsInteractionState state,
        CharacterOverviewState overview)
        => MatchesOverview(state, overview)
           && HasBudgetShape(state.ContactBudget, CharacterCreationContactBudgetIds.Contacts)
           && HasBudgetShape(
               state.HighPlacesBudget,
               CharacterCreationContactBudgetIds.FriendsInHighPlaces)
           && IsCanonicalDigest(state.SnapshotDigest)
           && IsCanonicalDigest(state.Binding.ContentDigest)
           && IsRawLowerDigest(state.Binding.AuxiliaryStateDigest)
           && IsCanonicalDigest(state.Binding.SourceDigest)
           && IsCanonicalDigest(state.Binding.RulesDigest)
           && IsCanonicalDigest(state.Binding.RuntimeDigest)
           && state.Blockers.All(blocker => !string.IsNullOrWhiteSpace(blocker))
           && state.Blockers.Distinct(StringComparer.Ordinal).Count() == state.Blockers.Count
           && state.Contacts.All(IsExactContact)
           && state.Contacts.Select(contact => contact.ContactId).Distinct().Count()
               == state.Contacts.Count;

    public static bool IsReady(
        CharacterCreationContactsInteractionState state,
        CharacterOverviewState overview)
        => IsBound(state, overview)
           && state.CanEdit
           && state.Blockers.Count == 0
           && IsExactBudget(state.ContactBudget, CharacterCreationContactBudgetIds.Contacts)
           && IsExactBudget(
               state.HighPlacesBudget,
               CharacterCreationContactBudgetIds.FriendsInHighPlaces);

    public static bool MatchesOverview(
        CharacterCreationContactsInteractionState state,
        CharacterOverviewState overview)
        => overview.Profile?.Created == false
           && overview.WorkspaceId is { } workspaceId
           && state.Binding.WorkspaceId == workspaceId
           && state.Binding.WorkspaceRevision == overview.ContentRevision
           && state.Binding.ContentRevision == overview.ContentRevision
           && state.Binding.SavedRevision == overview.SavedRevision
           && overview.CreationContacts is
           {
               Schema: CharacterCreationContactsSchemas.StateV1,
               StepId: CharacterCreationWizardStepIds.ContactsLifestyles,
               CharacterCreated: false
           } projected
           && BindingEquals(state.Binding, projected.Binding)
           && string.Equals(state.SnapshotDigest, projected.SnapshotDigest, StringComparison.Ordinal)
           && state.CanEdit == projected.CanEdit
           && state.Blockers.SequenceEqual(projected.Blockers, StringComparer.Ordinal)
           && BudgetEquals(state.ContactBudget, projected.ContactBudget)
           && BudgetEquals(state.HighPlacesBudget, projected.HighPlacesBudget)
           && ContactsEqual(state.Contacts, projected.Contacts);

    public static bool BindingEquals(
        CharacterCreationContactBinding left,
        CharacterCreationContactBinding right)
        => left.WorkspaceId == right.WorkspaceId
           && left.WorkspaceRevision == right.WorkspaceRevision
           && left.ContentRevision == right.ContentRevision
           && left.SavedRevision == right.SavedRevision
           && string.Equals(left.ContentDigest, right.ContentDigest, StringComparison.Ordinal)
           && string.Equals(
               left.AuxiliaryStateDigest,
               right.AuxiliaryStateDigest,
               StringComparison.Ordinal)
           && string.Equals(left.SourceDigest, right.SourceDigest, StringComparison.Ordinal)
           && string.Equals(left.RulesDigest, right.RulesDigest, StringComparison.Ordinal)
           && string.Equals(left.RuntimeDigest, right.RuntimeDigest, StringComparison.Ordinal);

    public static CharacterCreationContactProjection? ResolveUniqueContact(
        CharacterCreationContactsInteractionState state,
        Guid contactId)
    {
        CharacterCreationContactProjection[] matches = state.Contacts
            .Where(contact => contact.ContactId == contactId)
            .Take(2)
            .ToArray();
        return matches is [{ } contact] && IsExactContact(contact) ? contact : null;
    }

    public static bool IsExactContact(CharacterCreationContactProjection contact)
    {
        if (contact.ContactId == Guid.Empty
            || !IsCanonicalDigest(contact.ContactDigest)
            || contact.ContactPointCost < 0
            || !contact.SourceAnchorIds.SequenceEqual(
                CharacterCreationContactSourceAnchors.All,
                StringComparer.Ordinal)
            || contact.Fields.Count != CharacterCreationContactFieldIds.All.Count
            || !contact.Fields.Select(field => field.FieldId)
                .SequenceEqual(CharacterCreationContactFieldIds.All, StringComparer.Ordinal)
            || contact.Fields.Select(field => field.FieldId).Distinct(StringComparer.Ordinal).Count()
                != CharacterCreationContactFieldIds.All.Count)
        {
            return false;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [CharacterCreationContactFieldIds.Name] = contact.Identity.Name,
            [CharacterCreationContactFieldIds.Role] = contact.Identity.Role,
            [CharacterCreationContactFieldIds.Location] = contact.Identity.Location,
            [CharacterCreationContactFieldIds.Notes] = contact.Identity.Notes,
            [CharacterCreationContactFieldIds.CustomName] = contact.Identity.CustomName,
            [CharacterCreationContactFieldIds.Metatype] = contact.Identity.Metatype,
            [CharacterCreationContactFieldIds.Gender] = contact.Identity.Gender,
            [CharacterCreationContactFieldIds.Age] = contact.Identity.Age,
            [CharacterCreationContactFieldIds.ContactType] = contact.Identity.ContactType,
            [CharacterCreationContactFieldIds.PreferredPayment] = contact.Identity.PreferredPayment,
            [CharacterCreationContactFieldIds.HobbiesVice] = contact.Identity.HobbiesVice,
            [CharacterCreationContactFieldIds.PersonalLife] = contact.Identity.PersonalLife,
            [CharacterCreationContactFieldIds.GroupName] = contact.Identity.GroupName,
            [CharacterCreationContactFieldIds.Connection] = contact.Connection.ToString(CultureInfo.InvariantCulture),
            [CharacterCreationContactFieldIds.Loyalty] = contact.Loyalty.ToString(CultureInfo.InvariantCulture),
            [CharacterCreationContactFieldIds.Group] = contact.IsGroup.ToString(CultureInfo.InvariantCulture),
            [CharacterCreationContactFieldIds.Free] = contact.Free.ToString(CultureInfo.InvariantCulture),
            [CharacterCreationContactFieldIds.Family] = contact.Family.ToString(CultureInfo.InvariantCulture),
            [CharacterCreationContactFieldIds.Blackmail] = contact.Blackmail.ToString(CultureInfo.InvariantCulture)
        };
        return contact.Fields.All(field =>
            values.TryGetValue(field.FieldId, out string? expected)
            && string.Equals(field.SerializedValue, expected, StringComparison.Ordinal)
            && FieldShapeIsValid(field));
    }

    public static bool PreparedMatches(
        CharacterCreationContactPreparedPreview prepared,
        CharacterCreationContactsInteractionState state,
        CharacterOverviewState overview)
        => IsReady(state, overview)
           && BindingEquals(prepared.Binding, state.Binding)
           && string.Equals(
               prepared.ContactsSnapshotDigest,
               state.SnapshotDigest,
               StringComparison.Ordinal)
           && prepared.Edit.ContactId == prepared.ContactBefore.ContactId
           && prepared.ContactBefore.ContactId == prepared.ContactAfter.ContactId
           && ResolveUniqueContact(state, prepared.ContactBefore.ContactId) is { } live
           && ContactEquals(live, prepared.ContactBefore)
           && ContactsEqual(state.Contacts, prepared.ContactsBefore)
           && prepared.RequiresExplicitConfirmation
           && prepared.CanConfirm
           && prepared.Blockers.Count == 0
           && prepared.IdempotencyKey.Length is > 0 and <= 200
           && string.Equals(
               prepared.IdempotencyKey,
               prepared.IdempotencyKey.Trim(),
               StringComparison.Ordinal)
           && prepared.IdempotencyKey.All(character => char.IsLetterOrDigit(character)
               || character is '-' or '_' or '.' or ':' or '/')
           && IsCanonicalDigest(prepared.PreviewDigest)
           && IsExactContact(prepared.ContactAfter)
           && EditMatchesContacts(prepared)
           && WritePlanMatchesPrepared(prepared)
           && BudgetEquals(prepared.ContactBudgetBefore, state.ContactBudget)
           && BudgetEquals(prepared.HighPlacesBudgetBefore, state.HighPlacesBudget)
           && IsExactBudget(
               prepared.ContactBudgetAfter,
               CharacterCreationContactBudgetIds.Contacts)
           && IsExactBudget(
               prepared.HighPlacesBudgetAfter,
               CharacterCreationContactBudgetIds.FriendsInHighPlaces);

    public static bool ReceiptMatches(
        CharacterCreationContactPreparedPreview prepared,
        CharacterCreationContactReceipt receipt)
        => string.Equals(receipt.Schema, CharacterCreationContactsSchemas.ReceiptV1, StringComparison.Ordinal)
           && string.Equals(
               receipt.StepId,
               CharacterCreationWizardStepIds.ContactsLifestyles,
               StringComparison.Ordinal)
           && receipt.WorkspaceId == prepared.Binding.WorkspaceId
           && receipt.ContactId == prepared.ContactBefore.ContactId
           && receipt.ReceiptId.StartsWith("creation-contact-", StringComparison.Ordinal)
           && receipt.ReceiptId.Length == 41
           && receipt.PreviousWorkspaceRevision == prepared.Binding.WorkspaceRevision
           && receipt.WorkspaceRevision == prepared.Binding.WorkspaceRevision + 1
           && receipt.PreviousContentRevision == prepared.Binding.ContentRevision
           && receipt.ContentRevision == prepared.Binding.ContentRevision + 1
           && receipt.PreviousSavedRevision == prepared.Binding.SavedRevision
           && receipt.SavedRevision == receipt.ContentRevision
           && string.Equals(
               receipt.ContentDigestBefore,
               prepared.Binding.ContentDigest,
               StringComparison.Ordinal)
           && string.Equals(
               receipt.ContentDigestAfter,
               prepared.WritePlan.ContentDigestAfter,
               StringComparison.Ordinal)
           && string.Equals(receipt.SourceDigest, prepared.Binding.SourceDigest, StringComparison.Ordinal)
           && string.Equals(receipt.RulesDigest, prepared.Binding.RulesDigest, StringComparison.Ordinal)
           && string.Equals(receipt.RuntimeDigest, prepared.Binding.RuntimeDigest, StringComparison.Ordinal)
           && receipt.ContactPointsBefore == prepared.ContactBudgetBefore.Used
           && receipt.ContactPointsAfter == prepared.ContactBudgetAfter.Used
           && receipt.ContactPointsRemaining == prepared.ContactBudgetAfter.Remaining
           && receipt.HighPlacesPointsBefore == prepared.HighPlacesBudgetBefore.Used
           && receipt.HighPlacesPointsAfter == prepared.HighPlacesBudgetAfter.Used
           && receipt.HighPlacesPointsRemaining == prepared.HighPlacesBudgetAfter.Remaining
           && WritePlanEquals(receipt.WritePlan, prepared.WritePlan)
           && IsCanonicalDigest(receipt.IdempotencyKeyDigest)
           && IsCanonicalDigest(receipt.CommandDigest)
           && IsCanonicalDigest(receipt.ReceiptDigest)
           && !string.IsNullOrWhiteSpace(receipt.ReceiptId);

    public static bool RefreshedStateMatches(
        CharacterCreationContactPreparedPreview prepared,
        CharacterCreationContactReceipt receipt,
        CharacterCreationContactsInteractionState refreshed,
        CharacterOverviewState overview)
        => IsReady(refreshed, overview)
           && ReceiptMatches(prepared, receipt)
           && refreshed.Binding.WorkspaceRevision == receipt.WorkspaceRevision
           && refreshed.Binding.ContentRevision == receipt.ContentRevision
           && refreshed.Binding.SavedRevision == receipt.SavedRevision
           && string.Equals(
               refreshed.Binding.ContentDigest,
               receipt.ContentDigestAfter,
               StringComparison.Ordinal)
           && string.Equals(refreshed.Binding.SourceDigest, receipt.SourceDigest, StringComparison.Ordinal)
           && string.Equals(refreshed.Binding.RulesDigest, receipt.RulesDigest, StringComparison.Ordinal)
           && string.Equals(refreshed.Binding.RuntimeDigest, receipt.RuntimeDigest, StringComparison.Ordinal)
           && BudgetEquals(refreshed.ContactBudget, prepared.ContactBudgetAfter)
           && BudgetEquals(refreshed.HighPlacesBudget, prepared.HighPlacesBudgetAfter)
           && refreshed.Contacts.Count == prepared.ContactsBefore.Count
           && ResolveUniqueContact(refreshed, receipt.ContactId) is { } target
           && ContactEquals(target, prepared.ContactAfter)
           && prepared.ContactsBefore
               .Where(contact => contact.ContactId != receipt.ContactId)
               .All(before => ResolveUniqueContact(refreshed, before.ContactId) is { } sibling
                              && ContactEquals(before, sibling));

    private static bool ContactsEqual(
        IReadOnlyList<CharacterCreationContactProjection> left,
        IReadOnlyList<CharacterCreationContactProjection> right)
        => left.Count == right.Count
           && left.Zip(right).All(pair => ContactEquals(pair.First, pair.Second));

    private static bool ContactEquals(
        CharacterCreationContactProjection left,
        CharacterCreationContactProjection right)
        => left.ContactId == right.ContactId
           && string.Equals(left.ContactDigest, right.ContactDigest, StringComparison.Ordinal)
           && left.ContactPointCost == right.ContactPointCost
           && left.CountsAgainstContactBudget == right.CountsAgainstContactBudget
           && left.CountsAgainstHighPlacesBudget == right.CountsAgainstHighPlacesBudget
           && Equals(left.Identity, right.Identity)
           && left.Connection == right.Connection
           && left.Loyalty == right.Loyalty
           && left.IsGroup == right.IsGroup
           && left.Free == right.Free
           && left.Family == right.Family
           && left.Blackmail == right.Blackmail
           && left.SourceAnchorIds.SequenceEqual(right.SourceAnchorIds, StringComparer.Ordinal)
           && left.Fields.Count == right.Fields.Count
           && left.Fields.Zip(right.Fields).All(pair => FieldEquals(pair.First, pair.Second));

    private static bool FieldEquals(
        CharacterCreationContactFieldAuthority left,
        CharacterCreationContactFieldAuthority right)
        => string.Equals(left.FieldId, right.FieldId, StringComparison.Ordinal)
           && string.Equals(left.Label, right.Label, StringComparison.Ordinal)
           && string.Equals(left.ValueKind, right.ValueKind, StringComparison.Ordinal)
           && left.IsEditable == right.IsEditable
           && string.Equals(left.SerializedValue, right.SerializedValue, StringComparison.Ordinal)
           && left.Minimum == right.Minimum
           && left.Maximum == right.Maximum
           && left.Blockers.SequenceEqual(right.Blockers, StringComparer.Ordinal)
           && left.SourceAnchorIds.SequenceEqual(right.SourceAnchorIds, StringComparer.Ordinal)
           && left.LegalOptions.Count == right.LegalOptions.Count
           && left.LegalOptions.Zip(right.LegalOptions).All(pair =>
               OptionEquals(pair.First, pair.Second));

    private static bool BudgetEquals(
        CharacterCreationContactBudget left,
        CharacterCreationContactBudget right)
        => string.Equals(left.BudgetId, right.BudgetId, StringComparison.Ordinal)
           && left.Total == right.Total
           && left.Used == right.Used
           && left.Remaining == right.Remaining
           && left.Overspend == right.Overspend
           && left.IsExact == right.IsExact
           && left.Blockers.SequenceEqual(right.Blockers, StringComparer.Ordinal)
           && left.SourceAnchorIds.SequenceEqual(right.SourceAnchorIds, StringComparer.Ordinal);

    private static bool HasBudgetShape(CharacterCreationContactBudget budget, string budgetId)
        => string.Equals(budget.BudgetId, budgetId, StringComparison.Ordinal)
           && budget.Total >= 0
           && budget.Used >= 0
           && budget.Remaining == budget.Total - budget.Used
           && budget.Overspend == Math.Max(0, budget.Used - budget.Total)
           && budget.Blockers.All(blocker => !string.IsNullOrWhiteSpace(blocker))
           && budget.Blockers.Distinct(StringComparer.Ordinal).Count() == budget.Blockers.Count
           && budget.SourceAnchorIds.Count > 0
           && budget.SourceAnchorIds.All(anchor => !string.IsNullOrWhiteSpace(anchor))
           && (budget.IsExact || budget.Blockers.Count > 0);

    private static bool IsExactBudget(CharacterCreationContactBudget budget, string budgetId)
        => HasBudgetShape(budget, budgetId)
           && budget.IsExact
           && budget.Blockers.Count == 0;

    private static bool FieldShapeIsValid(CharacterCreationContactFieldAuthority field)
    {
        if (string.IsNullOrWhiteSpace(field.Label)
            || !field.SourceAnchorIds.SequenceEqual(
                CharacterCreationContactSourceAnchors.All,
                StringComparer.Ordinal)
            || field.Blockers.Any(string.IsNullOrWhiteSpace)
            || field.Blockers.Distinct(StringComparer.Ordinal).Count() != field.Blockers.Count
            || field.IsEditable != (field.Blockers.Count == 0))
        {
            return false;
        }

        if (string.Equals(field.ValueKind, CharacterCreationContactValueKinds.Text, StringComparison.Ordinal))
        {
            return field.Minimum == 0
                   && field.Maximum is > 0
                   && field.SerializedValue.Length <= field.Maximum
                   && string.Equals(field.SerializedValue, field.SerializedValue.Trim(), StringComparison.Ordinal)
                   && field.LegalOptions.Count == 0;
        }

        if (field.LegalOptions.Count == 0
            || field.LegalOptions.Select(option => option.SerializedValue)
                .Distinct(StringComparer.Ordinal).Count() != field.LegalOptions.Count
            || field.LegalOptions.Any(option => !OptionShapeIsValid(option, field.IsEditable))
            || field.LegalOptions.Count(option => string.Equals(
                option.SerializedValue,
                field.SerializedValue,
                StringComparison.Ordinal)) != 1)
        {
            return false;
        }

        if (string.Equals(field.ValueKind, CharacterCreationContactValueKinds.Integer, StringComparison.Ordinal))
        {
            return field.Minimum is int minimum
                   && field.Maximum is int maximum
                   && minimum <= maximum
                   && int.TryParse(
                       field.SerializedValue,
                       NumberStyles.Integer,
                       CultureInfo.InvariantCulture,
                       out int value)
                   && value >= minimum
                   && value <= maximum;
        }

        return string.Equals(field.ValueKind, CharacterCreationContactValueKinds.Boolean, StringComparison.Ordinal)
               && field.Minimum is null
               && field.Maximum is null
               && bool.TryParse(field.SerializedValue, out _)
               && field.LegalOptions.Count == 2
               && field.LegalOptions.Select(option => option.SerializedValue)
                   .SequenceEqual([bool.FalseString, bool.TrueString], StringComparer.Ordinal);
    }

    private static bool OptionShapeIsValid(
        CharacterCreationContactOption option,
        bool fieldIsEditable)
        => !string.IsNullOrWhiteSpace(option.OptionId)
           && !string.IsNullOrWhiteSpace(option.Label)
           && option.SerializedValue is not null
           && option.IsEnabled == fieldIsEditable
           && option.Blockers.All(blocker => !string.IsNullOrWhiteSpace(blocker))
           && option.Blockers.Distinct(StringComparer.Ordinal).Count() == option.Blockers.Count
           && option.IsEnabled == (option.Blockers.Count == 0)
           && option.SourceAnchorIds.SequenceEqual(
               CharacterCreationContactSourceAnchors.All,
               StringComparer.Ordinal);

    private static bool OptionEquals(
        CharacterCreationContactOption left,
        CharacterCreationContactOption right)
        => string.Equals(left.OptionId, right.OptionId, StringComparison.Ordinal)
           && string.Equals(left.Label, right.Label, StringComparison.Ordinal)
           && string.Equals(left.SerializedValue, right.SerializedValue, StringComparison.Ordinal)
           && left.IsEnabled == right.IsEnabled
           && left.Blockers.SequenceEqual(right.Blockers, StringComparer.Ordinal)
           && left.SourceAnchorIds.SequenceEqual(right.SourceAnchorIds, StringComparer.Ordinal);

    private static bool EditMatchesContacts(CharacterCreationContactPreparedPreview prepared)
        => prepared.Edit.ContactId == prepared.ContactBefore.ContactId
           && (prepared.Edit.Identity is null
               ? prepared.ContactAfter.Identity == prepared.ContactBefore.Identity
               : prepared.ContactAfter.Identity == prepared.Edit.Identity)
           && RequestedEquals(prepared.Edit.Connection, prepared.ContactBefore.Connection, prepared.ContactAfter.Connection)
           && RequestedEquals(prepared.Edit.Loyalty, prepared.ContactBefore.Loyalty, prepared.ContactAfter.Loyalty)
           && RequestedEquals(prepared.Edit.IsGroup, prepared.ContactBefore.IsGroup, prepared.ContactAfter.IsGroup)
           && RequestedEquals(prepared.Edit.Free, prepared.ContactBefore.Free, prepared.ContactAfter.Free)
           && RequestedEquals(prepared.Edit.Family, prepared.ContactBefore.Family, prepared.ContactAfter.Family)
           && RequestedEquals(prepared.Edit.Blackmail, prepared.ContactBefore.Blackmail, prepared.ContactAfter.Blackmail);

    private static bool RequestedEquals<T>(T? requested, T before, T after)
        where T : struct, IEquatable<T>
        => requested is T value ? value.Equals(after) : before.Equals(after);

    private static bool WritePlanMatchesPrepared(CharacterCreationContactPreparedPreview prepared)
    {
        CharacterCreationContactAtomicWritePlan plan = prepared.WritePlan;
        if (!string.Equals(plan.Schema, CharacterCreationContactsSchemas.WritePlanV1, StringComparison.Ordinal)
            || !string.Equals(plan.StepId, CharacterCreationWizardStepIds.ContactsLifestyles, StringComparison.Ordinal)
            || plan.ContactId != prepared.ContactBefore.ContactId
            || plan.Operations.Count == 0
            || !plan.Operations.Select(operation => operation.Order)
                .SequenceEqual(Enumerable.Range(1, plan.Operations.Count))
            || plan.Operations.Select(operation => operation.FieldId)
                .Distinct(StringComparer.Ordinal).Count() != plan.Operations.Count
            || !plan.PreservesUntouchedSiblingState
            || !plan.PreservesNestedState
            || !IsCanonicalDigest(plan.ContentDigestBefore)
            || !IsCanonicalDigest(plan.ContentDigestAfter)
            || string.Equals(plan.ContentDigestBefore, plan.ContentDigestAfter, StringComparison.Ordinal)
            || !string.Equals(plan.ContentDigestBefore, prepared.Binding.ContentDigest, StringComparison.Ordinal)
            || !IsCanonicalDigest(plan.UntouchedSiblingDigestBefore)
            || !string.Equals(
                plan.UntouchedSiblingDigestBefore,
                plan.UntouchedSiblingDigestAfter,
                StringComparison.Ordinal)
            || !IsCanonicalDigest(plan.NestedStateDigestBefore)
            || !string.Equals(
                plan.NestedStateDigestBefore,
                plan.NestedStateDigestAfter,
                StringComparison.Ordinal)
            || !IsCanonicalDigest(plan.PlanDigest))
        {
            return false;
        }

        Dictionary<string, CharacterCreationContactFieldAuthority> before =
            prepared.ContactBefore.Fields.ToDictionary(field => field.FieldId, StringComparer.Ordinal);
        Dictionary<string, CharacterCreationContactFieldAuthority> after =
            prepared.ContactAfter.Fields.ToDictionary(field => field.FieldId, StringComparer.Ordinal);
        string[] changedFieldIds = CharacterCreationContactFieldIds.All
            .Where(fieldId => !string.Equals(
                before[fieldId].SerializedValue,
                after[fieldId].SerializedValue,
                StringComparison.Ordinal))
            .ToArray();
        return plan.Operations.Select(operation => operation.FieldId)
                   .SequenceEqual(changedFieldIds, StringComparer.Ordinal)
               && plan.Operations.All(operation =>
                   CharacterCreationContactFieldIds.All.Contains(
                       operation.FieldId,
                       StringComparer.Ordinal)
                   && string.Equals(
                       operation.BeforeValue,
                       before[operation.FieldId].SerializedValue,
                       StringComparison.Ordinal)
                   && string.Equals(
                       operation.AfterValue,
                       after[operation.FieldId].SerializedValue,
                       StringComparison.Ordinal)
                   && !string.Equals(
                       operation.BeforeValue,
                       operation.AfterValue,
                       StringComparison.Ordinal)
                   && operation.SourceAnchorIds.SequenceEqual(
                       CharacterCreationContactSourceAnchors.All,
                       StringComparer.Ordinal));
    }

    private static bool WritePlanEquals(
        CharacterCreationContactAtomicWritePlan left,
        CharacterCreationContactAtomicWritePlan right)
        => string.Equals(left.Schema, right.Schema, StringComparison.Ordinal)
           && string.Equals(left.StepId, right.StepId, StringComparison.Ordinal)
           && left.ContactId == right.ContactId
           && string.Equals(left.ContentDigestBefore, right.ContentDigestBefore, StringComparison.Ordinal)
           && string.Equals(left.ContentDigestAfter, right.ContentDigestAfter, StringComparison.Ordinal)
           && string.Equals(left.UntouchedSiblingDigestBefore, right.UntouchedSiblingDigestBefore, StringComparison.Ordinal)
           && string.Equals(left.UntouchedSiblingDigestAfter, right.UntouchedSiblingDigestAfter, StringComparison.Ordinal)
           && string.Equals(left.NestedStateDigestBefore, right.NestedStateDigestBefore, StringComparison.Ordinal)
           && string.Equals(left.NestedStateDigestAfter, right.NestedStateDigestAfter, StringComparison.Ordinal)
           && left.PreservesUntouchedSiblingState == right.PreservesUntouchedSiblingState
           && left.PreservesNestedState == right.PreservesNestedState
           && string.Equals(left.PlanDigest, right.PlanDigest, StringComparison.Ordinal)
           && left.Operations.Count == right.Operations.Count
           && left.Operations.Zip(right.Operations).All(pair =>
               pair.First.Order == pair.Second.Order
               && string.Equals(pair.First.FieldId, pair.Second.FieldId, StringComparison.Ordinal)
               && string.Equals(pair.First.BeforeValue, pair.Second.BeforeValue, StringComparison.Ordinal)
               && string.Equals(pair.First.AfterValue, pair.Second.AfterValue, StringComparison.Ordinal)
               && pair.First.SourceAnchorIds.SequenceEqual(
                   pair.Second.SourceAnchorIds,
                   StringComparer.Ordinal));

    public static bool IsCanonicalDigest(string value)
        => value.Length == 71
           && value.StartsWith("sha256:", StringComparison.Ordinal)
           && value[7..].All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    public static bool IsRawLowerDigest(string value)
        => value.Length == 64
           && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
