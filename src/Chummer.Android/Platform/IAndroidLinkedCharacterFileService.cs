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
    CharacterLinkedDocument Identity)
{
    public string ContentSha256 { get; init; } = string.Empty;
}

public interface IAndroidLinkedCharacterFileService
{
    // A returned file belongs exclusively to this staging invocation until
    // dispatched. Returning an existing content-addressed/shared path is forbidden.
    // Return only after file data and the final directory entry are flushed.
    Task<AndroidStagedLinkedCharacter?> StageAsync(
        WorkspaceCollectionItemTarget target,
        CancellationToken cancellationToken);

    Task DeleteOwnedAsync(
        WorkspaceCollectionItemTarget target,
        string? fileName,
        CancellationToken cancellationToken);

    Task<bool> MatchesStagedFileAsync(WorkspaceCollectionItemTarget target, string fileName,
        string expectedSha256, CancellationToken cancellationToken) => Task.FromResult(false);
}

public sealed class AndroidLinkedCharacterFileService : IAndroidLinkedCharacterFileService
{
    private const string DirectoryName = "linked-characters";
    private readonly IAndroidDocumentService _documents;
    private readonly ICharacterLinkedDocumentCodec _codec;
    private readonly Func<string> _appDataDirectory;
    private readonly Func<Guid> _newFileId;
    private readonly Action<FileStream> _flushFile;
    private readonly Action<string> _syncDirectory;

    public AndroidLinkedCharacterFileService(
        IAndroidDocumentService documents,
        ICharacterLinkedDocumentCodec codec)
        : this(documents, codec, () => FileSystem.AppDataDirectory, Guid.NewGuid) { }

    internal AndroidLinkedCharacterFileService(IAndroidDocumentService documents,
        ICharacterLinkedDocumentCodec codec, Func<string> appDataDirectory, Func<Guid> newFileId,
        Action<FileStream>? flushFile = null, Action<string>? syncDirectory = null)
    {
        _documents = documents;
        _codec = codec;
        _appDataDirectory = appDataDirectory;
        _newFileId = newFileId;
        _flushFile = flushFile ?? (stream => stream.Flush(flushToDisk: true));
        _syncDirectory = syncDirectory ?? AndroidPrivateFileDurability.SyncDirectory;
    }

    public async Task<AndroidStagedLinkedCharacter?> StageAsync(
        WorkspaceCollectionItemTarget target,
        CancellationToken cancellationToken)
    {
        ValidateTarget(target);
        cancellationToken.ThrowIfCancellationRequested();
        AndroidDocument? selected = await _documents.OpenAsync(cancellationToken);
        if (selected is null)
        {
            return null;
        }

        try
        {
            // Parsing, hashing and synchronous durability barriers must not run
            // on Android's UI synchronization context. Once scheduled, always
            // join this work before clearing the selected buffer.
            return await Task.Run(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                string displayName = NormalizeDisplayName(selected.DisplayName);
                if (!_codec.TryDecode(displayName, selected.Content, out CharacterLinkedDocument? identity))
                {
                    throw new InvalidOperationException(
                        "Select a valid Chummer5 .chum5 or .chum5lz runner document.");
                }

                string extension = ResolveExtension(displayName);
                string targetPrefix = BuildTargetPrefix(target);
                string contentHash = Convert.ToHexStringLower(SHA256.HashData(selected.Content));
                string stagedFileName = $"{targetPrefix}-{contentHash[..16]}-{_newFileId():N}{extension}";
                string root = ResolveRoot();
                Directory.CreateDirectory(root);
                // The linked-characters directory itself may have been created
                // on this call. Persist its parent entry before any file publish.
                _syncDirectory(Path.GetDirectoryName(root)!);
                string finalPath = Path.Combine(root, stagedFileName);
                string temporaryPath = Path.Combine(root, $".{stagedFileName}.{Guid.NewGuid():N}.tmp");
                try
                {
                    await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew,
                        FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
                    {
                        await stream.WriteAsync(selected.Content, cancellationToken).ConfigureAwait(false);
                        _flushFile(stream);
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    File.Move(temporaryPath, finalPath, overwrite: false);
                    _syncDirectory(root);
                    // Do not throw for a late cancellation after this durable
                    // acknowledgement: return ownership for undispatched cleanup.
                }
                finally
                {
                    if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
                    // A failed directory acknowledgement after rename retains
                    // the exclusive final file, but never exposes a link to it.
                    // Reclamation needs separate reference/recovery authority.
                }

                return new AndroidStagedLinkedCharacter(
                    FileName: finalPath,
                    RelativeFileName: $"{DirectoryName}/{stagedFileName}",
                    DisplayName: displayName,
                    Identity: identity) { ContentSha256 = contentHash };
            }, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(selected.Content);
        }
    }

    public async Task<bool> MatchesStagedFileAsync(WorkspaceCollectionItemTarget target, string fileName,
        string expectedSha256, CancellationToken cancellationToken)
    {
        ValidateTarget(target);
        if (expectedSha256.Length != 64 || !expectedSha256.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f')
            || !TryResolveOwnedPath(target, fileName, out string path)) return false;
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = new FileInfo(path);
            if (!file.Exists || file.LinkTarget is not null || file.Length is <= 0 or > 8 * 1024 * 1024
                || new DirectoryInfo(ResolveRoot()).LinkTarget is not null) return false;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length != file.Length) return false;
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            Span<byte> buffer = stackalloc byte[8192];
            try
            {
                long remaining = file.Length;
                while (remaining > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int read = stream.Read(buffer[..(int)Math.Min(buffer.Length, remaining)]);
                    if (read == 0) return false;
                    digest.AppendData(buffer[..read]);
                    remaining -= read;
                }
                cancellationToken.ThrowIfCancellationRequested();
                return stream.ReadByte() == -1 && stream.Length == file.Length
                    && Convert.ToHexStringLower(digest.GetHashAndReset()) == expectedSha256;
            }
            finally { CryptographicOperations.ZeroMemory(buffer); }
        }, cancellationToken).ConfigureAwait(false);
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

    private bool TryResolveOwnedPath(
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

    private string ResolveRoot()
        => Path.GetFullPath(Path.Combine(_appDataDirectory(), DirectoryName));

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
