using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chummer.Contracts.LifeModules;

namespace Chummer.Android.Native;

// Private reading decoration, not a character mutation or provider receipt.
// Local selections never claim that a named provider produced the bytes.
internal sealed class OriginBookScene
{
    internal sealed record Metadata(string ChapterId, string ChapterDigest, string TextDigest,
        string AltText, string MediaType, string ImageDigest, int Length);
    private readonly byte[] _bytes;
    internal Metadata Identity { get; }

    private OriginBookScene(Metadata identity, byte[] bytes) { Identity = identity; _bytes = bytes; }

    internal static OriginBookScene Create(Metadata identity, byte[] bytes)
    {
        if (bytes is not { Length: > 0 and <= 4 * 1024 * 1024 }
            || string.IsNullOrWhiteSpace(identity.ChapterId) || identity.ChapterId.Length > 256
            || !IsDigest(identity.ChapterDigest) || !IsDigest(identity.TextDigest)
            || string.IsNullOrWhiteSpace(identity.AltText) || identity.AltText.Length > 1024)
            throw new InvalidDataException("The scene is invalid or oversized.");
        byte[] captured = bytes.ToArray();
        if (identity.Length != captured.Length || identity.ImageDigest != Hash(captured)
            || identity.MediaType != OriginBookEpub.ImageType(captured))
            throw new InvalidDataException("The scene bytes do not match their identity.");
        return new(identity, captured);
    }

    internal static OriginBookScene ForChapter(RetainedOriginBook book, OriginNarrativeChapterProjection chapter,
        string altText, byte[] bytes)
    {
        if (!book.Chapters.Contains(chapter)) throw new InvalidDataException("The scene chapter changed.");
        return Create(new(chapter.ChapterId, chapter.ChapterDigest, Hash(Encoding.UTF8.GetBytes(book.ChapterText(chapter))),
            altText, OriginBookEpub.ImageType(bytes), Hash(bytes), bytes.Length), bytes);
    }

    internal bool Matches(RetainedOriginBook book, OriginNarrativeChapterProjection chapter)
        => book.Chapters.Contains(chapter) && Identity.ChapterId == chapter.ChapterId
            && Identity.ChapterDigest == chapter.ChapterDigest
            && Identity.TextDigest == Hash(Encoding.UTF8.GetBytes(book.ChapterText(chapter)));

    internal Stream Open() => new MemoryStream(_bytes, writable: false);
    internal OriginBookScene WithDescription(string text) => Create(Identity with { AltText = text }, _bytes);
    internal OriginBookEpub.Illustration Export() => new(Identity.ChapterId, Identity.TextDigest, Identity.AltText, _bytes.ToArray());
    internal static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static bool IsDigest(string? text) => text is { Length: 64 }
        && text.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}

internal sealed class OriginBookScenes(string owner, string workspace, IEnumerable<OriginBookScene> scenes)
{
    internal string Owner { get; } = owner;
    internal string Workspace { get; } = workspace;
    internal IReadOnlyList<OriginBookScene> Scenes { get; } = Array.AsReadOnly(scenes.ToArray());
    internal string Digest => OriginBookScene.Hash(JsonSerializer.SerializeToUtf8Bytes(
        new { Owner, Workspace, Scenes = Scenes.Select(s => s.Identity) }));
}

/// <summary>Bounded, atomic offline scene storage. Use on a worker under an owner lease.</summary>
public sealed class OriginBookSceneStore(string stateDirectory)
{
    private const string Schema = "chummer.android.origin-book-scenes/v1";
    private const int MaximumBytes = 17 * 1024 * 1024;
    private sealed record Manifest(string Schema, string Owner, string Workspace,
        OriginBookScene.Metadata[] Scenes, string Digest);
    private readonly string _root = Path.Combine(stateDirectory, "origin-book-scenes");
    private readonly object _gate = new();

    internal OriginBookScenes Load(string owner, string workspace)
    {
        string path = PathFor(owner, workspace);
        if (!File.Exists(path)) return new(owner, workspace, []);
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (input.Length > MaximumBytes) throw new InvalidDataException("The scene archive is oversized.");
        using var zip = new ZipArchive(input, ZipArchiveMode.Read);
        if (zip.Entries.Count is < 1 or > 9 || zip.Entries[0].FullName != "manifest.json")
            throw new InvalidDataException("The scene archive has an invalid inventory.");
        var manifest = JsonSerializer.Deserialize<Manifest>(Read(zip.Entries[0], 64 * 1024));
        if (manifest?.Schema != Schema || manifest.Owner != owner || manifest.Workspace != workspace
            || manifest.Scenes is not { Length: <= 8 } || zip.Entries.Count != manifest.Scenes.Length + 1)
            throw new InvalidDataException("The scene archive belongs to another book or has invalid metadata.");
        var scenes = new List<OriginBookScene>();
        long total = 0;
        for (int i = 0; i < manifest.Scenes.Length; i++)
        {
            var entry = zip.Entries[i + 1];
            if (entry.FullName != $"scene-{i}" || (total += entry.Length) > 16 * 1024 * 1024
                || manifest.Scenes[i] is not { } identity)
                throw new InvalidDataException("The scene archive has invalid or oversized images.");
            scenes.Add(OriginBookScene.Create(identity, Read(entry, 4 * 1024 * 1024)));
        }
        var result = new OriginBookScenes(owner, workspace, scenes);
        if (!Valid(result) || result.Digest != manifest.Digest)
            throw new InvalidDataException("The scene archive changed identity.");
        return result;
    }

    internal OriginBookScenes Save(OriginBookScenes expected, OriginBookScenes next,
        Func<bool> stillCurrent, CancellationToken ct)
    {
        if (next.Owner != expected.Owner || next.Workspace != expected.Workspace || !Valid(next))
            throw new InvalidDataException("The scene collection changed identity or exceeds its limits.");
        byte[] manifestBytes = JsonSerializer.SerializeToUtf8Bytes(new Manifest(Schema, next.Owner, next.Workspace,
            next.Scenes.Select(s => s.Identity).ToArray(), next.Digest));
        if (manifestBytes.Length > 64 * 1024) throw new InvalidDataException("The scene metadata is oversized.");
        lock (_gate)
        {
            ct.ThrowIfCancellationRequested();
            if (!stillCurrent()) throw new OperationCanceledException("The book context changed.");
            Directory.CreateDirectory(_root);
            string path = PathFor(expected.Owner, expected.Workspace);
            using var custody = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            if (Load(expected.Owner, expected.Workspace).Digest != expected.Digest)
                throw new InvalidOperationException("The scene collection changed; reopen the book.");
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
                    {
                        using (var manifest = zip.CreateEntry("manifest.json").Open())
                            manifest.Write(manifestBytes);
                        for (int i = 0; i < next.Scenes.Count; i++)
                        {
                            using var entry = zip.CreateEntry($"scene-{i}", CompressionLevel.NoCompression).Open();
                            using var image = next.Scenes[i].Open();
                            image.CopyTo(entry);
                        }
                    }
                    output.Flush(flushToDisk: true);
                }
                ct.ThrowIfCancellationRequested();
                if (!stillCurrent()) throw new OperationCanceledException("The book context changed.");
                File.Move(temporary, path, overwrite: true);
                return next; // No cancellation after the durable commit boundary.
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    private static bool Valid(OriginBookScenes state) => state.Scenes.Count <= 8
        && state.Scenes.Sum(s => (long)s.Identity.Length) <= 16 * 1024 * 1024
        && state.Scenes.Select(s => s.Identity.ChapterId).Distinct(StringComparer.Ordinal).Count() == state.Scenes.Count;

    private static byte[] Read(ZipArchiveEntry entry, int maximum)
    {
        if (entry.Length < 1 || entry.Length > maximum) throw new InvalidDataException("Oversized scene entry.");
        using var source = entry.Open();
        byte[] bytes = new byte[checked((int)entry.Length)];
        source.ReadExactly(bytes);
        if (source.ReadByte() != -1) throw new InvalidDataException("Scene entry exceeds its declared length.");
        return bytes;
    }

    private string PathFor(string owner, string workspace)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner); ArgumentException.ThrowIfNullOrWhiteSpace(workspace);
        return Path.Combine(_root, OriginBookScene.Hash(Encoding.UTF8.GetBytes(owner + "\0" + workspace)) + ".zip");
    }
}
