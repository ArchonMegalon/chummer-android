using System.Security.Cryptography;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public interface ISr5TableWizardCheckpointBackend
{
    string Read();
    void Write(string payload);
    void Remove();
}

public enum Sr5TableWizardCheckpointReadStatus
{
    Empty,
    Ready,
    Invalid,
    Unavailable
}

public sealed record Sr5TableWizardCheckpointRead(
    Sr5TableWizardCheckpointReadStatus Status,
    Sr5TableWizardCheckpoint? Checkpoint);

/// <summary>
/// Durable reviewed-action store. Payloads are bounded, schema-validated, and verified by exact
/// read-back before navigation may enter review.
/// </summary>
public sealed class Sr5TableWizardCheckpointStore
{
    private const int MaximumPayloadBytes = 32 * 1024;
    private const int MaximumEncodedCharacters = 48 * 1024;
    private readonly ISr5TableWizardCheckpointBackend _backend;
    private readonly object _sync = new();

    public Sr5TableWizardCheckpointStore(ISr5TableWizardCheckpointBackend backend)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    public Sr5TableWizardCheckpointRead Read()
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
                return new(Sr5TableWizardCheckpointReadStatus.Unavailable, null);
            }
            if (string.IsNullOrEmpty(encoded))
                return new(Sr5TableWizardCheckpointReadStatus.Empty, null);
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
                    || !Sr5TableWizardSession.TryDeserializeCheckpoint(
                        payload,
                        out Sr5TableWizardCheckpoint? checkpoint)
                    || checkpoint is null)
                {
                    return InvalidAndRemove();
                }
                return new(Sr5TableWizardCheckpointReadStatus.Ready, checkpoint);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(payload);
            }
        }
    }

    public bool TryWrite(Sr5TableWizardSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        lock (_sync)
            return TryWriteLocked(session);
    }

    private bool TryWriteLocked(Sr5TableWizardSession session)
    {
        byte[] payload;
        try
        {
            payload = Sr5TableWizardSession.SerializeCheckpoint(session.CreateCheckpoint());
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
            if (encoded.Length > MaximumEncodedCharacters)
                return false;
            try
            {
                _backend.Write(encoded);
                return string.Equals(_backend.Read(), encoded, StringComparison.Ordinal);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                return false;
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
        {
            try
            {
                _backend.Remove();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // A subsequent read stays unavailable/invalid and therefore keeps apply closed.
            }
        }
    }

    private Sr5TableWizardCheckpointRead InvalidAndRemove()
    {
        try
        {
            _backend.Remove();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return new(Sr5TableWizardCheckpointReadStatus.Unavailable, null);
        }
        return new(Sr5TableWizardCheckpointReadStatus.Invalid, null);
    }
}
