using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Android.Native;

public sealed class AndroidSr5CareerCustomDrugWorkspaceStore(IWorkspaceStore store)
    : ISr5CareerCustomDrugWorkspaceStore
{
    public Sr5CareerCustomDrugWorkspaceSnapshot? Read(CharacterWorkspaceId workspaceId)
    {
        WorkspaceStoreReadResult read = store.Get(workspaceId);
        return read.Success && read.Value is { } value
            ? new Sr5CareerCustomDrugWorkspaceSnapshot(
                workspaceId,
                value.ContentRevision,
                value.SavedRevision,
                value.Document)
            : null;
    }

    public Sr5CareerCustomDrugWorkspaceWriteResult ReplaceAndCheckpoint(
        Sr5CareerCustomDrugWorkspaceSnapshot expected,
        string characterXml)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentException.ThrowIfNullOrWhiteSpace(characterXml);
        WorkspaceDocument replacement = expected.Document with
        {
            State = expected.Document.State with { Payload = characterXml }
        };
        WorkspaceStoreMutationResult result = store.ReplaceWorkspaceDocumentAndCheckpoint(
            expected.WorkspaceId,
            expected.ContentRevision,
            replacement);
        return result.Entry is { } entry
            ? new Sr5CareerCustomDrugWorkspaceWriteResult(
                result.Success,
                result.Outcome == WorkspaceOperationOutcome.Conflict,
                entry.ContentRevision,
                entry.SavedRevision,
                result.Error ?? string.Empty)
            : new Sr5CareerCustomDrugWorkspaceWriteResult(
                Applied: false,
                result.Outcome == WorkspaceOperationOutcome.Conflict,
                expected.ContentRevision,
                expected.SavedRevision,
                result.Error ?? string.Empty);
    }
}

public sealed class PreferencesSr5CareerCustomDrugRecipeCheckpointStore
    : ISr5CareerCustomDrugRecipeCheckpointStore
{
    private const string KeyPrefix = "chummer.android.sr5-career-custom-drug-recipe.v1.";
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = null };

    public Sr5CareerCustomDrugRecipeCheckpoint? Read(CharacterWorkspaceId workspaceId)
    {
        string payload = Preferences.Default.Get(Key(workspaceId), string.Empty);
        if (string.IsNullOrWhiteSpace(payload))
            return null;
        try
        {
            Sr5CareerCustomDrugRecipeCheckpoint? checkpoint =
                JsonSerializer.Deserialize<Sr5CareerCustomDrugRecipeCheckpoint>(payload, Json);
            if (checkpoint?.BelongsTo(workspaceId) == true)
                return checkpoint;
            if (checkpoint is not null
                && checkpoint.WorkspaceId == workspaceId
                && checkpoint.Phase is Sr5CareerCustomDrugRecipePhase.Applying
                    or Sr5CareerCustomDrugRecipePhase.RecoveryUnknown)
            {
                // Preserve uncertain outcomes even when local payload bytes are
                // incomplete. The service may use a still-valid command for
                // read-only receipt lookup, but can never reopen or replay it.
                return ProtectedUnknown(workspaceId, checkpoint);
            }
        }
        catch (JsonException)
        {
            // Corrupt local state grants no recipe authority and is discarded below.
        }
        Preferences.Default.Remove(Key(workspaceId));
        return null;
    }

    public void Write(Sr5CareerCustomDrugRecipeCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (!checkpoint.BelongsTo(checkpoint.WorkspaceId))
            throw new InvalidOperationException("The custom-drug recipe checkpoint is not structurally bound.");
        Preferences.Default.Set(
            Key(checkpoint.WorkspaceId),
            JsonSerializer.Serialize(checkpoint, Json));
    }

    public void Clear(CharacterWorkspaceId workspaceId)
        => Preferences.Default.Remove(Key(workspaceId));

    private static Sr5CareerCustomDrugRecipeCheckpoint ProtectedUnknown(
        CharacterWorkspaceId workspaceId,
        Sr5CareerCustomDrugRecipeCheckpoint checkpoint)
        => new(
            Sr5CareerCustomDrugRecipeSchemas.CheckpointV1,
            workspaceId,
            Math.Max(0, checkpoint.BoundContentRevision),
            checkpoint.BoundCharacterDigest ?? string.Empty,
            checkpoint.BoundCatalogDigest ?? string.Empty,
            checkpoint.BoundRulesDigest ?? string.Empty,
            SafeSelection(checkpoint.Selection),
            Sr5CareerCustomDrugRecipePhase.RecoveryUnknown,
            checkpoint.Command,
            checkpoint.Receipt);

    private static CharacterCustomDrugSelection SafeSelection(
        CharacterCustomDrugSelection? selection)
        => selection is not null && selection.Components is not null
            ? selection with
            {
                Components = selection.Components
                    .Where(static component => component is not null)
                    .Take(256)
                    .ToArray()
            }
            : Sr5CareerCustomDrugRecipeService.EmptySelection;

    private static string Key(CharacterWorkspaceId workspaceId)
    {
        string digest = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(workspaceId.Value)))
            .ToLowerInvariant();
        return KeyPrefix + digest;
    }
}
