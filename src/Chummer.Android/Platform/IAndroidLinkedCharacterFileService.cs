using System.Security.Cryptography;
using System.Text;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Platform;

public sealed record AndroidStagedLinkedCharacter(
    string FileName,
    string RelativeFileName,
    string DisplayName,
    CharacterLinkedDocument Identity);

public interface IAndroidLinkedCharacterFileService
{
    Task<AndroidStagedLinkedCharacter?> StageAsync(
        WorkspaceCollectionItemTarget target,
        CancellationToken cancellationToken);

    Task DeleteOwnedAsync(
        WorkspaceCollectionItemTarget target,
        string? fileName,
        CancellationToken cancellationToken);
}

public sealed class AndroidLinkedCharacterFileService : IAndroidLinkedCharacterFileService
{
    private const string DirectoryName = "linked-characters";
    private readonly IAndroidDocumentService _documents;
    private readonly ICharacterLinkedDocumentCodec _codec;

    public AndroidLinkedCharacterFileService(
        IAndroidDocumentService documents,
        ICharacterLinkedDocumentCodec codec)
    {
        _documents = documents;
        _codec = codec;
    }

    public async Task<AndroidStagedLinkedCharacter?> StageAsync(
        WorkspaceCollectionItemTarget target,
        CancellationToken cancellationToken)
    {
        ValidateTarget(target);
        AndroidDocument? selected = await _documents.OpenAsync(cancellationToken);
        if (selected is null)
        {
            return null;
        }

        try
        {
            string displayName = NormalizeDisplayName(selected.DisplayName);
            if (!_codec.TryDecode(displayName, selected.Content, out CharacterLinkedDocument? identity))
            {
                throw new InvalidOperationException(
                    "Select a valid Chummer5 .chum5 or .chum5lz runner document.");
            }

            string extension = ResolveExtension(displayName);
            string targetPrefix = BuildTargetPrefix(target);
            string contentHash = Convert.ToHexString(SHA256.HashData(selected.Content))
                .ToLowerInvariant()[..16];
            string stagedFileName = $"{targetPrefix}-{contentHash}{extension}";
            string root = ResolveRoot();
            Directory.CreateDirectory(root);
            string finalPath = Path.Combine(root, stagedFileName);
            string temporaryPath = Path.Combine(root, $".{stagedFileName}.{Guid.NewGuid():N}.tmp");
            try
            {
                await File.WriteAllBytesAsync(temporaryPath, selected.Content, cancellationToken);
                File.Move(temporaryPath, finalPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }

            return new AndroidStagedLinkedCharacter(
                FileName: finalPath,
                RelativeFileName: $"{DirectoryName}/{stagedFileName}",
                DisplayName: displayName,
                Identity: identity);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(selected.Content);
        }
    }

    public Task DeleteOwnedAsync(
        WorkspaceCollectionItemTarget target,
        string? fileName,
        CancellationToken cancellationToken)
    {
        ValidateTarget(target);
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryResolveOwnedPath(target, fileName, out string? ownedPath))
        {
            return Task.CompletedTask;
        }

        File.Delete(ownedPath);
        return Task.CompletedTask;
    }

    private static bool TryResolveOwnedPath(
        WorkspaceCollectionItemTarget target,
        string? fileName,
        out string ownedPath)
    {
        ownedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(fileName) || !Path.IsPathFullyQualified(fileName))
        {
            return false;
        }

        string root = ResolveRoot();
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(fileName);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        string? parent = Path.GetDirectoryName(fullPath);
        string leaf = Path.GetFileName(fullPath);
        string extension = Path.GetExtension(leaf);
        if (!string.Equals(parent, root, StringComparison.Ordinal)
            || !leaf.StartsWith(BuildTargetPrefix(target) + "-", StringComparison.Ordinal)
            || !IsSupportedExtension(extension))
        {
            return false;
        }

        ownedPath = fullPath;
        return true;
    }

    private static string ResolveRoot()
        => Path.GetFullPath(Path.Combine(FileSystem.AppDataDirectory, DirectoryName));

    private static string BuildTargetPrefix(WorkspaceCollectionItemTarget target)
    {
        string identity = $"{target.Kind}:{target.ItemId.Trim().ToLowerInvariant()}";
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))
            .ToLowerInvariant()[..16];
        return $"{target.Kind.ToString().ToLowerInvariant()}-{hash}";
    }

    private static string NormalizeDisplayName(string value)
    {
        string normalized = (value ?? string.Empty).Trim().Replace('\\', '/');
        string displayName = normalized[(normalized.LastIndexOf('/') + 1)..];
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > 512)
        {
            throw new InvalidOperationException("The selected runner document has an invalid display name.");
        }

        ResolveExtension(displayName);
        return displayName;
    }

    private static string ResolveExtension(string fileName)
    {
        string extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!IsSupportedExtension(extension))
        {
            throw new InvalidOperationException("Linked runners must use a .chum5 or .chum5lz document.");
        }

        return extension;
    }

    private static bool IsSupportedExtension(string extension)
        => extension is ".chum5" or ".chum5lz";

    private static void ValidateTarget(WorkspaceCollectionItemTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.Kind is not (WorkspaceCollectionKind.Contact or WorkspaceCollectionKind.Pet)
            || target.NestedKind is not null
            || !string.IsNullOrWhiteSpace(target.NestedItemId)
            || string.IsNullOrWhiteSpace(target.ItemId))
        {
            throw new InvalidOperationException("Linked runners require a stable top-level Contact or Pet target.");
        }
    }
}
