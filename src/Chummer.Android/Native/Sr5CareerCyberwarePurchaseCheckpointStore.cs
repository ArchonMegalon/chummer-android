using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chummer.Contracts.Workspaces;

namespace Chummer.Android.Native;

public sealed class PreferencesSr5CareerCyberwarePurchaseCheckpointStore
    : ISr5CareerCyberwarePurchaseCheckpointStore
{
    private const string KeyPrefix = "chummer.android.sr5-career-cyberware-purchase.v1.";
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = null };

    public Sr5CareerCyberwarePurchaseCheckpoint? Read(CharacterWorkspaceId workspaceId)
    {
        string payload = Preferences.Default.Get(Key(workspaceId), string.Empty);
        if (string.IsNullOrWhiteSpace(payload))
            return null;
        try
        {
            Sr5CareerCyberwarePurchaseCheckpoint? checkpoint =
                JsonSerializer.Deserialize<Sr5CareerCyberwarePurchaseCheckpoint>(payload, Json);
            if (checkpoint?.BelongsTo(workspaceId) == true)
                return checkpoint;
        }
        catch (JsonException)
        {
            // Corrupt local state grants no purchase authority and is discarded below.
        }
        Preferences.Default.Remove(Key(workspaceId));
        return null;
    }

    public void Write(Sr5CareerCyberwarePurchaseCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (!checkpoint.BelongsTo(checkpoint.WorkspaceId))
            throw new InvalidOperationException("The Cyberware purchase checkpoint is not structurally bound.");
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
