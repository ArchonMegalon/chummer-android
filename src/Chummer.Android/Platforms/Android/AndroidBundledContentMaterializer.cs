using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Android.Content.Res;

namespace Chummer.Android;

public static class AndroidBundledContentMaterializer
{
    internal const string PackagedContentRoot = "chummer-content";
    internal const string ManifestAssetPath = $"{PackagedContentRoot}/manifest.json";
    internal const string ManifestSchema = "chummer.android.content-bundle/v1";
    internal const string CanonicalCoreRevision = "5e98a92c6fd012aea0d9664f6619adc94e36ce8d";
    private const string MaterializedContentDirectory = "canonical-content";

    public static string Materialize()
    {
        AssetManager assets = global::Android.App.Application.Context.Assets
            ?? throw new InvalidOperationException("Android packaged assets are unavailable.");
        ContentManifest manifest = LoadAndValidateManifest(assets);
        string contentContainer = Path.Combine(
            FileSystem.AppDataDirectory,
            MaterializedContentDirectory);
        EnsureRegularDirectory(contentContainer);
        CleanupInterruptedStaging(contentContainer);
        string destinationRoot = Path.Combine(contentContainer, manifest.BundleDigest);

        if (Directory.Exists(destinationRoot))
        {
            try
            {
                VerifyMaterializedContent(destinationRoot, manifest);
                return destinationRoot;
            }
            catch (InvalidDataException)
            {
                QuarantineInvalidGeneration(contentContainer, destinationRoot, manifest.BundleDigest);
            }
        }
        else if (File.Exists(destinationRoot))
        {
            QuarantineInvalidGeneration(contentContainer, destinationRoot, manifest.BundleDigest);
        }

        string stagingRoot = Path.Combine(
            contentContainer,
            $".staging-{manifest.BundleDigest}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingRoot);
        RejectReparsePoint(stagingRoot);
        try
        {
            foreach (ContentManifestFile file in manifest.Files)
            {
                CopyAssetFile(assets, file.Path, stagingRoot);
            }
            VerifyMaterializedContent(stagingRoot, manifest);

            try
            {
                Directory.Move(stagingRoot, destinationRoot);
            }
            catch (IOException) when (Directory.Exists(destinationRoot))
            {
                VerifyMaterializedContent(destinationRoot, manifest);
                DeleteOwnedTree(stagingRoot);
            }
            return destinationRoot;
        }
        catch
        {
            if (Directory.Exists(stagingRoot))
            {
                DeleteOwnedTree(stagingRoot);
            }
            throw;
        }
    }

    private static ContentManifest LoadAndValidateManifest(AssetManager assets)
    {
        using Stream manifestStream = assets.Open(ManifestAssetPath, Access.Streaming)
            ?? throw new FileNotFoundException(
                $"Packaged Chummer content manifest '{ManifestAssetPath}' could not be opened.");
        ContentManifest? manifest = JsonSerializer.Deserialize<ContentManifest>(
            manifestStream,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (manifest is null
            || !string.Equals(manifest.Schema, ManifestSchema, StringComparison.Ordinal)
            || !string.Equals(
                manifest.CoreRevision,
                CanonicalCoreRevision,
                StringComparison.Ordinal)
            || manifest.Files is null
            || manifest.Files.Count == 0)
        {
            throw new InvalidDataException("The packaged Chummer content manifest is invalid.");
        }

        ValidateLowercaseSha256(manifest.BundleDigest, "bundleDigest");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? previousPath = null;
        foreach (ContentManifestFile file in manifest.Files)
        {
            ValidateRelativePath(file.Path);
            if (!seen.Add(file.Path)
                || previousPath is not null
                    && string.CompareOrdinal(previousPath, file.Path) >= 0)
            {
                throw new InvalidDataException(
                    "Packaged Chummer content manifest paths must be unique and ordinally sorted.");
            }
            if (file.Size < 0)
            {
                throw new InvalidDataException(
                    $"Packaged Chummer content asset '{file.Path}' has a negative size.");
            }
            ValidateLowercaseSha256(file.Sha256, $"files[{file.Path}].sha256");
            previousPath = file.Path;
        }

        if (!seen.Contains("data/lifemodules.xml")
            || !seen.Any(path => path.StartsWith("lang/", StringComparison.Ordinal)
                && path.EndsWith(".xml", StringComparison.Ordinal)))
        {
            throw new InvalidDataException(
                "The packaged Chummer content manifest lacks required data or language catalogs.");
        }

        string computedDigest = ComputeBundleDigest(manifest.Files);
        if (!string.Equals(manifest.BundleDigest, computedDigest, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Packaged Chummer content manifest digest mismatch: expected "
                + $"'{manifest.BundleDigest}', computed '{computedDigest}'.");
        }
        return manifest;
    }

    private static void CopyAssetFile(
        AssetManager assets,
        string relativePath,
        string stagingRoot)
    {
        string destinationPath = ResolveDestinationPath(stagingRoot, relativePath);
        string? parent = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
            RejectReparsePoint(parent);
        }

        string assetPath = $"{PackagedContentRoot}/{relativePath}";
        using Stream source = assets.Open(assetPath, Access.Streaming)
            ?? throw new FileNotFoundException(
                $"Packaged Chummer content asset '{assetPath}' could not be opened.");
        using FileStream destination = new(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);
        source.CopyTo(destination);
        destination.Flush(flushToDisk: true);
    }

    private static void VerifyMaterializedContent(
        string contentRoot,
        ContentManifest manifest)
    {
        RejectReparsePoint(contentRoot);
        Dictionary<string, ContentManifestFile> expected = manifest.Files
            .ToDictionary(file => file.Path, StringComparer.Ordinal);
        string[] actualFiles = EnumerateRegularFiles(contentRoot, contentRoot)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        if (!actualFiles.SequenceEqual(expected.Keys.OrderBy(path => path, StringComparer.Ordinal)))
        {
            throw new InvalidDataException(
                "The materialized Chummer content file set does not match its manifest.");
        }

        foreach (string relativePath in actualFiles)
        {
            ContentManifestFile declared = expected[relativePath];
            string fullPath = ResolveDestinationPath(contentRoot, relativePath);
            var info = new FileInfo(fullPath);
            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new InvalidDataException(
                    $"Materialized Chummer content '{relativePath}' must not be a reparse point.");
            }
            if (info.Length != declared.Size)
            {
                throw new InvalidDataException(
                    $"Materialized Chummer content size mismatch for '{relativePath}'.");
            }

            using FileStream stream = File.OpenRead(fullPath);
            string actualDigest = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if (!string.Equals(actualDigest, declared.Sha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Materialized Chummer content digest mismatch for '{relativePath}'.");
            }
        }
    }

    private static IEnumerable<string> EnumerateRegularFiles(
        string contentRoot,
        string directoryPath)
    {
        RejectReparsePoint(directoryPath);
        foreach (FileSystemInfo entry in new DirectoryInfo(directoryPath)
                     .EnumerateFileSystemInfos()
                     .OrderBy(item => item.Name, StringComparer.Ordinal))
        {
            if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new InvalidDataException(
                    $"Materialized Chummer content '{entry.FullName}' must not be a reparse point.");
            }
            if (entry is DirectoryInfo directory)
            {
                foreach (string child in EnumerateRegularFiles(contentRoot, directory.FullName))
                {
                    yield return child;
                }
                continue;
            }
            if (entry is not FileInfo)
            {
                throw new InvalidDataException(
                    $"Materialized Chummer content '{entry.FullName}' is not a regular file.");
            }
            yield return Path.GetRelativePath(contentRoot, entry.FullName)
                .Replace(Path.DirectorySeparatorChar, '/');
        }
    }

    private static string ComputeBundleDigest(IReadOnlyList<ContentManifestFile> files)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes($"{ManifestSchema}\n{CanonicalCoreRevision}\n"));
        foreach (ContentManifestFile file in files)
        {
            byte[] line = Encoding.UTF8.GetBytes(
                $"{file.Path}\0{file.Size}\0{file.Sha256}\n");
            hash.AppendData(line);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static string ResolveDestinationPath(string contentRoot, string relativePath)
    {
        ValidateRelativePath(relativePath);
        string normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        string fullRoot = Path.GetFullPath(contentRoot);
        string destination = Path.GetFullPath(Path.Combine(fullRoot, normalized));
        string prefix = fullRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;
        if (!destination.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Packaged Chummer content path '{relativePath}' escapes its destination root.");
        }
        return destination;
    }

    private static void ValidateRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)
            || relativePath.StartsWith("/", StringComparison.Ordinal)
            || relativePath.Contains('\\', StringComparison.Ordinal)
            || relativePath.Contains(':', StringComparison.Ordinal)
            || relativePath.Split('/').Any(segment => segment is "" or "." or "..")
            || !(relativePath.StartsWith("data/", StringComparison.Ordinal)
                || relativePath.StartsWith("lang/", StringComparison.Ordinal)))
        {
            throw new InvalidDataException(
                $"Packaged Chummer content path '{relativePath}' is not a safe canonical path.");
        }
    }

    private static void ValidateLowercaseSha256(string value, string field)
    {
        string candidate = value ?? string.Empty;
        if (candidate.Length != 64
            || candidate.Any(character => !Uri.IsHexDigit(character))
            || !string.Equals(candidate, candidate.ToLowerInvariant(), StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Packaged Chummer content manifest field '{field}' is not a lowercase SHA-256 digest.");
        }
    }

    private static void EnsureRegularDirectory(string path)
    {
        if (File.Exists(path) && !Directory.Exists(path))
        {
            throw new InvalidDataException($"Content directory '{path}' is occupied by a file.");
        }
        if (Directory.Exists(path))
        {
            RejectReparsePoint(path);
            return;
        }
        Directory.CreateDirectory(path);
        RejectReparsePoint(path);
    }

    private static void RejectReparsePoint(string path)
    {
        if (!Directory.Exists(path) && !File.Exists(path))
        {
            throw new InvalidDataException($"Required content path '{path}' is unavailable.");
        }
        if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidDataException($"Content path '{path}' must not be a reparse point.");
        }
    }

    private static void QuarantineInvalidGeneration(
        string contentContainer,
        string generationPath,
        string bundleDigest)
    {
        RejectReparsePoint(generationPath);
        string quarantineRoot = Path.Combine(
            contentContainer,
            $".invalid-{bundleDigest}-{Guid.NewGuid():N}");
        if (Directory.Exists(generationPath))
        {
            Directory.Move(generationPath, quarantineRoot);
        }
        else
        {
            File.Move(generationPath, quarantineRoot);
        }
    }

    private static void CleanupInterruptedStaging(string contentContainer)
    {
        RejectReparsePoint(contentContainer);
        foreach (FileSystemInfo entry in new DirectoryInfo(contentContainer)
                     .EnumerateFileSystemInfos(
                         ".staging-*",
                         SearchOption.TopDirectoryOnly)
                     .OrderBy(item => item.Name, StringComparer.Ordinal))
        {
            if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint)
                || entry is not DirectoryInfo directory)
            {
                throw new InvalidDataException(
                    $"Interrupted staging path '{entry.FullName}' is not a regular owned directory.");
            }
            DeleteOwnedTree(directory.FullName);
        }
    }

    private static void DeleteOwnedTree(string root)
    {
        RejectReparsePoint(root);
        foreach (FileSystemInfo entry in new DirectoryInfo(root).EnumerateFileSystemInfos())
        {
            if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new InvalidDataException(
                    $"Owned staging path '{entry.FullName}' became a reparse point.");
            }
            if (entry is DirectoryInfo directory)
            {
                DeleteOwnedTree(directory.FullName);
            }
            else
            {
                entry.Delete();
            }
        }
        Directory.Delete(root, recursive: false);
    }

    private sealed record ContentManifest(
        string Schema,
        string CoreRevision,
        string BundleDigest,
        IReadOnlyList<ContentManifestFile> Files);

    private sealed record ContentManifestFile(
        string Path,
        long Size,
        string Sha256);
}
