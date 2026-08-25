using System.Text.Json;
using Microsoft.Maui.Storage;

namespace Chummer.Android.Native;

internal static class Sr5CareerDraftCheckpointStore
{
    private const string StorageKey = "sr5.career.active-skill.draft.v1";

    public static bool TrySave(Sr5CareerDraftCheckpoint checkpoint, out string blocker)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        try
        {
            Preferences.Default.Set(StorageKey, JsonSerializer.Serialize(checkpoint));
            blocker = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            blocker = $"The reviewed draft could not be checkpointed: {exception.Message}";
            return false;
        }
    }

    public static bool TryLoad(out Sr5CareerDraftCheckpoint checkpoint, out string blocker)
    {
        checkpoint = null!;
        try
        {
            string payload = Preferences.Default.Get(StorageKey, string.Empty);
            if (string.IsNullOrWhiteSpace(payload))
            {
                blocker = string.Empty;
                return false;
            }

            checkpoint = JsonSerializer.Deserialize<Sr5CareerDraftCheckpoint>(payload)!;
            if (checkpoint is null)
            {
                blocker = "The saved Career draft is unreadable.";
                return false;
            }
            blocker = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            blocker = $"The saved Career draft is unreadable: {exception.Message}";
            return false;
        }
    }

    public static bool TryClear(string expectedIdempotencyKey, out string blocker)
    {
        try
        {
            string payload = Preferences.Default.Get(StorageKey, string.Empty);
            if (string.IsNullOrWhiteSpace(payload))
            {
                blocker = string.Empty;
                return true;
            }

            Sr5CareerDraftCheckpoint? checkpoint =
                JsonSerializer.Deserialize<Sr5CareerDraftCheckpoint>(payload);
            if (checkpoint is null
                || !string.Equals(
                    checkpoint.IdempotencyKey,
                    expectedIdempotencyKey,
                    StringComparison.Ordinal))
            {
                blocker = "A different Career draft now owns the recovery checkpoint.";
                return false;
            }

            Preferences.Default.Remove(StorageKey);
            blocker = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            blocker = $"The saved Career draft could not be cleared: {exception.Message}";
            return false;
        }
    }
}
