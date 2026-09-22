using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chummer.Presentation.OriginBooks;

namespace Chummer.Android.Native;

internal sealed record OriginBookReadingChapter(string ChapterId, OriginBookProseDraft? Selected, OriginBookProseDraft? Pending);
internal sealed record OriginBookReadingState(string Owner, string Workspace, IReadOnlyList<OriginBookReadingChapter> Chapters)
{
    internal string Digest => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(this))).ToLowerInvariant();
}

/// <summary>
/// App-private reading editions only. Called synchronously on a worker while
/// holding the captured owner lease. No workspace document or rules writes.
/// </summary>
public sealed class OriginBookReadingStore(string stateDirectory)
{
    private const int MaximumBytes = 2 * 1024 * 1024;
    private const string Schema = "chummer.android.origin-reading-edition/v1";
    private sealed record FileState(string Schema, OriginBookReadingState State, string Digest);
    private readonly string _root = Path.Combine(stateDirectory, "origin-reading-editions");
    private readonly object _gate = new();

    internal OriginBookReadingState Load(string owner, string workspace)
    {
        string path = PathFor(owner, workspace);
        if (!File.Exists(path)) return new(owner, workspace, []);
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (file.Length > MaximumBytes) throw new InvalidDataException("The reading edition is oversized.");
        byte[] bytes = new byte[MaximumBytes + 1];
        int used = 0, read;
        while (used < bytes.Length && (read = file.Read(bytes, used, bytes.Length - used)) != 0) used += read;
        if (used > MaximumBytes) throw new InvalidDataException("The reading edition is oversized.");
        var saved = JsonSerializer.Deserialize<FileState>(bytes.AsSpan(0, used));
        if (saved?.Schema != Schema || saved.State is not { } state || state.Owner != owner || state.Workspace != workspace
            || !Valid(state) || saved.Digest != state.Digest)
            throw new InvalidDataException("The reading edition has an invalid identity or content binding.");
        return state;
    }

    internal OriginBookReadingState Save(OriginBookReadingState expected, OriginBookReadingState next,
        Func<bool> stillCurrent, CancellationToken ct)
    {
        if (next.Owner != expected.Owner || next.Workspace != expected.Workspace || !Valid(next))
            throw new InvalidDataException("The reading edition changed identity.");
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new FileState(Schema, next, next.Digest));
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("The reading edition is oversized.");
        lock (_gate)
        {
            Directory.CreateDirectory(_root);
            string path = PathFor(expected.Owner, expected.Workspace);
            using var custody = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            if (Load(expected.Owner, expected.Workspace).Digest != expected.Digest)
                throw new InvalidOperationException("The reading edition changed; reopen it before reviewing.");
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { output.Write(bytes); output.Flush(flushToDisk: true); }
                ct.ThrowIfCancellationRequested();
                if (!stillCurrent()) throw new OperationCanceledException("The book context changed.");
                File.Move(temporary, path, overwrite: true);
                // After the commit boundary, return the saved result even if
                // cancellation arrives. Reopening reads the same exact edition.
                return next;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    private static bool Valid(OriginBookReadingState state)
        => state.Chapters is { Count: <= 128 }
            && state.Chapters.All(c => c is not null)
            && state.Chapters.Select(c => c.ChapterId).Distinct(StringComparer.Ordinal).Count() == state.Chapters.Count
            && state.Chapters.All(c => !string.IsNullOrWhiteSpace(c.ChapterId)
                && (c.Selected is null || c.Selected.IsValid() && c.Selected.ChapterId == c.ChapterId)
                && (c.Pending is null || c.Pending.IsValid() && c.Pending.ChapterId == c.ChapterId));

    private string PathFor(string owner, string workspace)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner); ArgumentException.ThrowIfNullOrWhiteSpace(workspace);
        return Path.Combine(_root, Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(owner + "\0" + workspace))) + ".json");
    }
}
