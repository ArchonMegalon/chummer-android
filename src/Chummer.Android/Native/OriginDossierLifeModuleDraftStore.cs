using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chummer.Contracts.LifeModules;
using Chummer.Presentation.OriginBooks;

namespace Chummer.Android.Native;

/// <summary>
/// Atomic, app-private persistence for the user's sealed Origin Dossier
/// timeline. Loading performs only storage identity checks; Core Restore must
/// still rebind the returned checkpoint to live rules authority.
/// </summary>
public sealed class FileOriginDossierDraftTimelineStore : IOriginDossierDraftTimelineStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly string _directory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileOriginDossierDraftTimelineStore(string stateDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateDirectory);
        _directory = Path.Combine(stateDirectory, "origin-dossier-drafts");
    }

    public async Task<LifeModuleOriginDossierDraftCheckpoint?> LoadAsync(
        string ownerId,
        string workspaceId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(ownerId, workspaceId);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string path = ResolvePath(ownerId, workspaceId);
            if (!File.Exists(path))
                return null;
            try
            {
                string json = await File.ReadAllTextAsync(path, cancellationToken)
                    .ConfigureAwait(false);
                LifeModuleOriginDossierDraftCheckpoint? checkpoint =
                    JsonSerializer.Deserialize<LifeModuleOriginDossierDraftCheckpoint>(
                        json,
                        JsonOptions);
                return checkpoint is not null
                       && string.Equals(checkpoint.OwnerId, ownerId, StringComparison.Ordinal)
                       && string.Equals(checkpoint.WorkspaceId, workspaceId, StringComparison.Ordinal)
                       && string.Equals(
                           checkpoint.Schema,
                           OriginDossierSchemas.DraftCheckpointV1,
                           StringComparison.Ordinal)
                       && !string.IsNullOrWhiteSpace(checkpoint.CheckpointDigest)
                    ? checkpoint
                    : null;
            }
            catch (Exception exception) when (exception is IOException
                                              or UnauthorizedAccessException
                                              or JsonException
                                              or NotSupportedException)
            {
                return null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        LifeModuleOriginDossierDraftCheckpoint checkpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ValidateIdentity(checkpoint.OwnerId, checkpoint.WorkspaceId);
        if (!string.Equals(
                checkpoint.Schema,
                OriginDossierSchemas.DraftCheckpointV1,
                StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(checkpoint.CheckpointDigest))
        {
            throw new InvalidOperationException(
                "Only a sealed Origin Dossier draft checkpoint can be persisted.");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? temporaryPath = null;
        try
        {
            Directory.CreateDirectory(_directory);
            string path = ResolvePath(checkpoint.OwnerId, checkpoint.WorkspaceId);
            temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            string json = JsonSerializer.Serialize(checkpoint, JsonOptions);
            await File.WriteAllTextAsync(temporaryPath, json, cancellationToken)
                .ConfigureAwait(false);
            File.Move(temporaryPath, path, overwrite: true);
            temporaryPath = null;
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (IOException)
                {
                    // A leftover temp file is never considered a checkpoint.
                }
            }
            _gate.Release();
        }
    }

    public async Task DeleteAsync(
        string ownerId,
        string workspaceId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(ownerId, workspaceId);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            File.Delete(ResolvePath(ownerId, workspaceId));
        }
        finally
        {
            _gate.Release();
        }
    }

    private string ResolvePath(string ownerId, string workspaceId)
    {
        string identity = ownerId + "\0" + workspaceId;
        string digest = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
        return Path.Combine(_directory, digest + ".json");
    }

    private static void ValidateIdentity(string ownerId, string workspaceId)
    {
        if (string.IsNullOrWhiteSpace(ownerId)
            || !string.Equals(ownerId, ownerId.Trim(), StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(workspaceId)
            || !string.Equals(workspaceId, workspaceId.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Origin Dossier owner and workspace identities must be exact.");
        }
    }
}
