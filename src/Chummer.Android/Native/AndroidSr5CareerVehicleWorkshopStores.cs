using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Android.Native;

public sealed class AndroidSr5CareerVehicleWorkshopWorkspaceStore(IWorkspaceStore store)
    : ISr5CareerVehicleWorkshopWorkspaceStore
{
    public Sr5CareerVehicleWorkshopWorkspaceSnapshot? Read(CharacterWorkspaceId workspaceId)
    {
        WorkspaceStoreReadResult read = store.Get(workspaceId);
        return read.Success && read.Value is { } value
            ? new Sr5CareerVehicleWorkshopWorkspaceSnapshot(
                workspaceId, value.ContentRevision, value.SavedRevision, value.Document)
            : null;
    }

    public Sr5CareerVehicleWorkshopWorkspaceWriteResult ReplaceAndCheckpoint(
        Sr5CareerVehicleWorkshopWorkspaceSnapshot expected,
        string characterXml)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentException.ThrowIfNullOrWhiteSpace(characterXml);
        WorkspaceDocument replacement = expected.Document with
        {
            State = expected.Document.State with { Payload = characterXml }
        };
        WorkspaceStoreMutationResult result = store.ReplaceWorkspaceDocumentAndCheckpoint(
            expected.WorkspaceId, expected.ContentRevision, replacement);
        return result.Entry is { } entry
            ? new Sr5CareerVehicleWorkshopWorkspaceWriteResult(
                result.Success,
                result.Outcome == WorkspaceOperationOutcome.Conflict,
                entry.ContentRevision,
                entry.SavedRevision,
                result.Error ?? string.Empty)
            : new Sr5CareerVehicleWorkshopWorkspaceWriteResult(
                false,
                result.Outcome == WorkspaceOperationOutcome.Conflict,
                expected.ContentRevision,
                expected.SavedRevision,
                result.Error ?? string.Empty);
    }
}

public sealed class PreferencesSr5CareerVehicleWorkshopCheckpointStore
    : ISr5CareerVehicleWorkshopCheckpointStore
{
    private const string KeyPrefix = "chummer.android.sr5-career-vehicle-workshop.v1.";
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = null };

    public Sr5CareerVehicleWorkshopCheckpoint? Read(CharacterWorkspaceId workspaceId)
    {
        string payload = Preferences.Default.Get(Key(workspaceId), string.Empty);
        if (string.IsNullOrWhiteSpace(payload))
            return null;
        try
        {
            Sr5CareerVehicleWorkshopCheckpoint? checkpoint =
                JsonSerializer.Deserialize<Sr5CareerVehicleWorkshopCheckpoint>(payload, Json);
            if (checkpoint?.BelongsTo(workspaceId) == true)
                return checkpoint;
            if (checkpoint is not null
                && checkpoint.WorkspaceId == workspaceId
                && checkpoint.Phase is Sr5CareerVehicleWorkshopPhase.Applying
                    or Sr5CareerVehicleWorkshopPhase.RecoveryUnknown)
                return ProtectedUnknown(workspaceId, checkpoint);
        }
        catch (JsonException)
        {
            // Corrupt drafts grant no authority and are removed below.
        }
        Preferences.Default.Remove(Key(workspaceId));
        return null;
    }

    public void Write(Sr5CareerVehicleWorkshopCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (!checkpoint.BelongsTo(checkpoint.WorkspaceId))
            throw new InvalidOperationException("The vehicle workshop checkpoint is not structurally bound.");
        Preferences.Default.Set(Key(checkpoint.WorkspaceId), JsonSerializer.Serialize(checkpoint, Json));
    }

    public void Clear(CharacterWorkspaceId workspaceId)
        => Preferences.Default.Remove(Key(workspaceId));

    private static Sr5CareerVehicleWorkshopCheckpoint ProtectedUnknown(
        CharacterWorkspaceId workspaceId,
        Sr5CareerVehicleWorkshopCheckpoint checkpoint)
        => checkpoint with
        {
            SchemaId = Sr5CareerVehicleWorkshopSchemas.CheckpointV1,
            WorkspaceId = workspaceId,
            BoundContentRevision = Math.Max(0, checkpoint.BoundContentRevision),
            BoundCharacterDigest = checkpoint.BoundCharacterDigest ?? string.Empty,
            BoundCatalogDigest = checkpoint.BoundCatalogDigest ?? string.Empty,
            Selection = checkpoint.Selection is null
                ? Sr5CareerVehicleWorkshopService.EmptySelection
                : checkpoint.Selection with
                {
                    CustomName = checkpoint.Selection.CustomName ?? string.Empty,
                    GmAuthorityDigest = checkpoint.Selection.GmAuthorityDigest ?? string.Empty,
                    Modifications = (checkpoint.Selection.Modifications ?? []).Take(256).ToArray(),
                    WeaponMounts = []
                },
            Phase = Sr5CareerVehicleWorkshopPhase.RecoveryUnknown
        };

    private static string Key(CharacterWorkspaceId workspaceId)
        => KeyPrefix + Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(workspaceId.Value))).ToLowerInvariant();
}
