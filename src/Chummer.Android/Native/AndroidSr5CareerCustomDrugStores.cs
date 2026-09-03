using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chummer.Application.Workspaces;
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

    private static string Key(CharacterWorkspaceId workspaceId)
    {
        string digest = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(workspaceId.Value)))
            .ToLowerInvariant();
        return KeyPrefix + digest;
    }
}
