using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Android.Native;

public sealed class PreferencesSr5CustomDrugLabCheckpointStore : ISr5CustomDrugLabCheckpointStore
{
    private const string KeyPrefix = "chummer.android.sr5-custom-drug.v1.";
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = null
    };

    public Sr5CustomDrugLabCheckpoint? Read(
        CharacterWorkspaceId workspaceId,
        CharacterCustomDrugContext context)
    {
        string payload = Preferences.Default.Get(Key(workspaceId, context), string.Empty);
        if (string.IsNullOrWhiteSpace(payload))
            return null;
        try
        {
            Sr5CustomDrugLabCheckpoint? checkpoint =
                JsonSerializer.Deserialize<Sr5CustomDrugLabCheckpoint>(payload, Json);
            if (checkpoint?.BelongsTo(workspaceId, context) == true)
                return checkpoint;
        }
        catch (JsonException)
        {
            // Corrupt local resume state grants no authority and is discarded below.
        }
        Preferences.Default.Remove(Key(workspaceId, context));
        return null;
    }

    public void Write(Sr5CustomDrugLabCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (!checkpoint.BelongsTo(checkpoint.WorkspaceId, checkpoint.Context))
            throw new InvalidOperationException("The custom-drug checkpoint is not structurally bound.");
        Preferences.Default.Set(
            Key(checkpoint.WorkspaceId, checkpoint.Context),
            JsonSerializer.Serialize(checkpoint, Json));
    }

    public void Clear(CharacterWorkspaceId workspaceId, CharacterCustomDrugContext context)
        => Preferences.Default.Remove(Key(workspaceId, context));

    private static string Key(CharacterWorkspaceId workspaceId, CharacterCustomDrugContext context)
    {
        string identity = $"{workspaceId.Value}\n{context}";
        string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))
            .ToLowerInvariant();
        return KeyPrefix + digest;
    }
}
