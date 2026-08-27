using System.Text.Json;
using System.Text.Json.Serialization;
using Chummer.Contracts.Characters;

namespace Chummer.Android.Native;

public static class Sr5VehicleWorkshopRoutes
{
    public const string Catalog = "sr5-career/vehicle-workshop/catalog";
    public const string FactoryModifications = "sr5-career/vehicle-workshop/factory-modifications";
    public const string Modifications = "sr5-career/vehicle-workshop/modifications";
    public const string WeaponMounts = "sr5-career/vehicle-workshop/weapon-mounts";
    public const string Review = "sr5-career/vehicle-workshop/review";
    public const string Receipt = "sr5-career/vehicle-workshop/receipt";
    public const string Recovery = "sr5-career/vehicle-workshop/recovery";

    public static bool IsKnown(string routeId)
        => routeId is Catalog or FactoryModifications or Modifications or WeaponMounts or Review or Receipt or Recovery;
}

public enum Sr5VehicleWorkshopCheckpointStage
{
    Draft,
    PendingCommit,
    Receipt,
    PendingUndo,
    Undone
}

public sealed record Sr5VehicleWorkshopDraftBinding(
    string WorkspaceId,
    long ContentRevision,
    string CharacterDigest,
    string CatalogDigest);

public sealed record Sr5VehicleWorkshopModificationDraft(
    CharacterVehicleModificationSourceId SourceId,
    CharacterVehicleModificationInstanceId InstanceId,
    int Rating);

public sealed record Sr5VehicleWorkshopMountComponentDraft(
    CharacterVehicleWeaponMountComponentSourceId SourceId,
    CharacterVehicleWeaponMountComponentInstanceId InstanceId);

public sealed record Sr5VehicleWorkshopMountDraft(
    CharacterVehicleWeaponMountInstanceId InstanceId,
    IReadOnlyList<Sr5VehicleWorkshopMountComponentDraft> Components);

/// <summary>
/// Restart-safe native draft. It stores only typed source/instance identities and user answers;
/// catalog facts and all economic/rules calculations are always re-read from Core.
/// </summary>
public sealed record Sr5VehicleWorkshopDraft(
    Sr5VehicleWorkshopDraftBinding Binding,
    string RouteId,
    CharacterVehicleChassisSourceId? ChassisSourceId,
    CharacterVehicleInstanceId NewVehicleInstanceId,
    string CustomName,
    string GmAuthorityDigest,
    IReadOnlyList<Sr5VehicleWorkshopModificationDraft> Modifications,
    IReadOnlyList<Sr5VehicleWorkshopMountDraft> WeaponMounts,
    string QuoteDigest)
{
    public static Sr5VehicleWorkshopDraft Create(
        string workspaceId,
        CharacterVehicleWorkshopPreparation preparation)
        => new(
            new Sr5VehicleWorkshopDraftBinding(
                workspaceId,
                preparation.ContentRevision,
                preparation.CharacterDigest,
                preparation.CatalogDigest),
            Sr5VehicleWorkshopRoutes.Catalog,
            ChassisSourceId: null,
            new CharacterVehicleInstanceId(Guid.NewGuid()),
            string.Empty,
            string.Empty,
            [],
            [],
            string.Empty);

    public bool Matches(
        string workspaceId,
        CharacterVehicleWorkshopPreparation preparation)
        => string.Equals(Binding.WorkspaceId, workspaceId, StringComparison.Ordinal)
           && Binding.ContentRevision == preparation.ContentRevision
           && CharacterVehicleWorkshopRules.FixedEquals(
               Binding.CharacterDigest,
               preparation.CharacterDigest)
           && CharacterVehicleWorkshopRules.FixedEquals(
               Binding.CatalogDigest,
               preparation.CatalogDigest);

    public bool TryCreateSelection(out CharacterVehicleWorkshopSelection selection)
    {
        if (!IsValid(out _)
            || ChassisSourceId is not { Value: { } chassisId }
            || chassisId == Guid.Empty)
        {
            selection = EmptySelection();
            return false;
        }

        selection = new CharacterVehicleWorkshopSelection(
            ChassisSourceId.Value,
            NewVehicleInstanceId,
            CustomName,
            GmAuthorityDigest,
            Modifications.Select(item => new CharacterVehicleWorkshopModificationSelection(
                item.SourceId,
                item.InstanceId,
                item.Rating)).ToArray(),
            WeaponMounts.Select(mount => new CharacterVehicleWeaponMountSelection(
                mount.InstanceId,
                mount.Components.Select(component =>
                    new CharacterVehicleWeaponMountComponentSelection(
                        component.SourceId,
                        component.InstanceId)).ToArray())).ToArray());
        return true;
    }

    public bool IsValid(out string reason)
    {
        reason = string.Empty;
        if (Binding is null
            || string.IsNullOrWhiteSpace(Binding.WorkspaceId)
            || Binding.WorkspaceId.Length > 512
            || Binding.ContentRevision < 0
            || !CharacterVehicleWorkshopRules.IsCanonicalDigest(Binding.CharacterDigest)
            || !CharacterVehicleWorkshopRules.IsCanonicalDigest(Binding.CatalogDigest)
            || !Sr5VehicleWorkshopRoutes.IsKnown(RouteId)
            || NewVehicleInstanceId.Value == Guid.Empty
            || CustomName is null
            || CustomName.Length > CharacterVehicleWorkshopRules.MaximumCustomNameLength
            || !string.Equals(CustomName, CustomName.Trim(), StringComparison.Ordinal)
            || GmAuthorityDigest is null
            || (GmAuthorityDigest.Length != 0
                && !CharacterVehicleWorkshopRules.IsCanonicalDigest(GmAuthorityDigest))
            || Modifications is null
            || WeaponMounts is null
            || Modifications.Count > 256
            || WeaponMounts.Count > 32
            || QuoteDigest is null
            || (QuoteDigest.Length != 0
                && !CharacterVehicleWorkshopRules.IsCanonicalDigest(QuoteDigest)))
        {
            reason = "The saved Vehicle & Drone Workshop draft binding is invalid.";
            return false;
        }

        if (ChassisSourceId is { Value: var invalidChassisSourceId }
            && invalidChassisSourceId == Guid.Empty)
        {
            reason = "A selected workshop chassis must have a valid typed source identity.";
            return false;
        }

        var sourceIds = ChassisSourceId is { Value: var chassisSourceId }
            ? new List<Guid> { chassisSourceId }
            : [];
        var instanceIds = new List<Guid> { NewVehicleInstanceId.Value };
        foreach (Sr5VehicleWorkshopModificationDraft? modification in Modifications)
        {
            if (modification is null
                || modification.SourceId.Value == Guid.Empty
                || modification.InstanceId.Value == Guid.Empty
                || modification.Rating <= 0)
            {
                reason = "A saved workshop modification has an invalid typed identity or rating.";
                return false;
            }
            sourceIds.Add(modification.SourceId.Value);
            instanceIds.Add(modification.InstanceId.Value);
        }

        if (sourceIds.Distinct().Count() != sourceIds.Count)
        {
            reason = "A saved workshop draft cannot select the same modification source twice.";
            return false;
        }

        foreach (Sr5VehicleWorkshopMountDraft? mount in WeaponMounts)
        {
            if (mount is null
                || mount.InstanceId.Value == Guid.Empty
                || mount.Components is null
                || mount.Components.Count > 4)
            {
                reason = "A saved weapon mount has an invalid typed composition.";
                return false;
            }
            instanceIds.Add(mount.InstanceId.Value);
            var mountSourceIds = new List<Guid>();
            foreach (Sr5VehicleWorkshopMountComponentDraft? component in mount.Components)
            {
                if (component is null
                    || component.SourceId.Value == Guid.Empty
                    || component.InstanceId.Value == Guid.Empty)
                {
                    reason = "A saved weapon-mount component has an invalid typed identity.";
                    return false;
                }
                sourceIds.Add(component.SourceId.Value);
                mountSourceIds.Add(component.SourceId.Value);
                instanceIds.Add(component.InstanceId.Value);
            }
            if (mountSourceIds.Distinct().Count() != mountSourceIds.Count)
            {
                reason = "A saved weapon mount cannot select the same component source twice.";
                return false;
            }
        }

        if (instanceIds.Distinct().Count() != instanceIds.Count
            || sourceIds.Intersect(instanceIds).Any())
        {
            reason = "Saved workshop instance identities must be distinct from one another and from source identities.";
            return false;
        }
        return true;
    }

    private static CharacterVehicleWorkshopSelection EmptySelection()
        => new(default, default, string.Empty, string.Empty, [], []);
}

public sealed record Sr5VehicleWorkshopCheckpoint(
    int SchemaVersion,
    Sr5VehicleWorkshopCheckpointStage Stage,
    Sr5VehicleWorkshopDraft Draft,
    CharacterVehicleWorkshopCommitCommand? Command,
    CharacterVehicleWorkshopCommitReceipt? Receipt,
    long ExpectedOutputRevision,
    string ExpectedOutputDigest,
    string BlockReason)
{
    public const int CurrentSchemaVersion = 2;

    public static Sr5VehicleWorkshopCheckpoint ForDraft(Sr5VehicleWorkshopDraft draft)
        => new(CurrentSchemaVersion, Sr5VehicleWorkshopCheckpointStage.Draft, draft,
            null, null, 0, string.Empty, string.Empty);

    public bool IsValid(out string reason)
    {
        reason = string.Empty;
        if (SchemaVersion != CurrentSchemaVersion
            || Draft is null
            || !Draft.IsValid(out reason)
            || BlockReason is null
            || ExpectedOutputDigest is null
            || BlockReason.Length > 2000)
        {
            reason = reason.Length == 0
                ? "The saved Vehicle & Drone Workshop checkpoint is invalid."
                : reason;
            return false;
        }

        if (Stage == Sr5VehicleWorkshopCheckpointStage.Draft)
            return Command is null && Receipt is null
                   && ExpectedOutputRevision == 0 && ExpectedOutputDigest.Length == 0;

        if (Stage == Sr5VehicleWorkshopCheckpointStage.Undone)
        {
            return Command is not null
                   && Receipt is not null
                   && ExpectedOutputRevision > 0
                   && CharacterVehicleWorkshopRules.IsCanonicalDigest(ExpectedOutputDigest)
                   && ValidateCommandAndReceipt(requireReceipt: true, out reason);
        }

        if (Command is null
            || ExpectedOutputRevision <= 0
            || !CharacterVehicleWorkshopRules.IsCanonicalDigest(ExpectedOutputDigest)
            || !ValidateCommandAndReceipt(
                requireReceipt: Stage is Sr5VehicleWorkshopCheckpointStage.Receipt
                    or Sr5VehicleWorkshopCheckpointStage.PendingUndo,
                out reason))
        {
            return false;
        }

        return Stage switch
        {
            Sr5VehicleWorkshopCheckpointStage.PendingCommit => Receipt is null,
            Sr5VehicleWorkshopCheckpointStage.Receipt => Receipt is not null,
            Sr5VehicleWorkshopCheckpointStage.PendingUndo => Receipt is not null,
            _ => false
        };
    }

    private bool ValidateCommandAndReceipt(bool requireReceipt, out string reason)
    {
        reason = string.Empty;
        if (Command is null
            || Command.Selection is null
            || !Draft.TryCreateSelection(out CharacterVehicleWorkshopSelection selection)
            || Command.ExpectedContentRevision != Draft.Binding.ContentRevision
            || !CharacterVehicleWorkshopRules.FixedEquals(
                Command.ExpectedCharacterDigest,
                Draft.Binding.CharacterDigest)
            || !CharacterVehicleWorkshopRules.FixedEquals(
                Command.ExpectedCatalogDigest,
                Draft.Binding.CatalogDigest)
            || !CharacterVehicleWorkshopRules.FixedEquals(
                Command.ExpectedQuoteDigest,
                Draft.QuoteDigest)
            || string.IsNullOrWhiteSpace(Command.IdempotencyKey)
            || Command.IdempotencyKey.Length > CharacterVehicleWorkshopRules.MaximumIdempotencyKeyLength
            || Command.NewExpenseId == Guid.Empty
            || CharacterVehicleWorkshopRules.ComputeCommandDigest(Command)
               != CharacterVehicleWorkshopRules.ComputeCommandDigest(Command with { Selection = selection }))
        {
            reason = "The saved workshop command no longer matches its typed draft.";
            return false;
        }

        if (!requireReceipt)
            return Receipt is null;

        if (Receipt is null
            || Receipt.VehicleInstanceId != selection.NewVehicleInstanceId
            || Receipt.ExpenseId != Command.NewExpenseId
            || !CharacterVehicleWorkshopRules.FixedEquals(
                Receipt.CatalogDigest,
                Command.ExpectedCatalogDigest)
            || !CharacterVehicleWorkshopRules.FixedEquals(
                Receipt.QuoteDigest,
                Command.ExpectedQuoteDigest)
            || !CharacterVehicleWorkshopRules.FixedEquals(
                Receipt.IdempotencyKeyDigest,
                CharacterVehicleWorkshopRules.ComputeIdempotencyKeyDigest(Command.IdempotencyKey))
            || !CharacterVehicleWorkshopRules.FixedEquals(
                Receipt.CommandDigest,
                CharacterVehicleWorkshopRules.ComputeCommandDigest(Command))
            || !CharacterVehicleWorkshopRules.FixedEquals(
                Receipt.ReceiptDigest,
                CharacterVehicleWorkshopRules.ComputeReceiptDigest(Receipt)))
        {
            reason = "The saved workshop receipt is absent, altered, or bound to another command.";
            return false;
        }
        return true;
    }
}

public interface ISr5VehicleWorkshopCheckpointBackend
{
    string Read();
    void Write(string payload);
    void Remove();
}

public sealed class Sr5VehicleWorkshopCheckpointStore
{
    private const int MaximumPayloadCharacters = 512 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly ISr5VehicleWorkshopCheckpointBackend _backend;

    public Sr5VehicleWorkshopCheckpointStore(ISr5VehicleWorkshopCheckpointBackend backend)
        => _backend = backend ?? throw new ArgumentNullException(nameof(backend));

    public bool TryRead(out Sr5VehicleWorkshopCheckpoint? checkpoint, out string reason)
    {
        checkpoint = null;
        reason = string.Empty;
        string payload;
        try
        {
            payload = _backend.Read() ?? string.Empty;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            reason = "The saved Vehicle & Drone Workshop draft could not be read.";
            return false;
        }
        if (payload.Length == 0)
            return true;
        if (payload.Length > MaximumPayloadCharacters)
        {
            reason = "The saved Vehicle & Drone Workshop draft exceeds its bounded size.";
            return false;
        }

        try
        {
            checkpoint = JsonSerializer.Deserialize<Sr5VehicleWorkshopCheckpoint>(payload, JsonOptions);
            if (checkpoint is null || !checkpoint.IsValid(out reason))
            {
                checkpoint = null;
                return false;
            }
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            reason = "The saved Vehicle & Drone Workshop draft is malformed.";
            checkpoint = null;
            return false;
        }
    }

    public bool TryWrite(Sr5VehicleWorkshopCheckpoint checkpoint, out string reason)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (!checkpoint.IsValid(out reason))
            return false;
        try
        {
            string payload = JsonSerializer.Serialize(checkpoint, JsonOptions);
            if (payload.Length > MaximumPayloadCharacters)
            {
                reason = "The Vehicle & Drone Workshop draft exceeds its bounded size.";
                return false;
            }
            _backend.Write(payload);
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            reason = "The Vehicle & Drone Workshop draft could not be saved durably.";
            return false;
        }
    }

    public void Remove()
    {
        try
        {
            _backend.Remove();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A failed removal cannot grant authority; the next bounded read validates again.
        }
    }
}
