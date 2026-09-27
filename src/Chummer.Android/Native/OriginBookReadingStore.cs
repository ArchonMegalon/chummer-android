using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Chummer.Presentation.OriginBooks;
using Chummer.Run.Contracts.Community;

namespace Chummer.Android.Native;

/// <summary>Optional player-authored opening brief, not a rules decision.</summary>
internal sealed record OriginStoryProfile(string? Gender = null, string? Pronouns = null,
    string? Tone = null, string? Motivation = null, string? ImportantPerson = null)
{
    internal bool IsEmpty => Gender is null && Pronouns is null && Tone is null
        && Motivation is null && ImportantPerson is null;
    internal bool IsValid => (Gender is null or "male" or "female" or "other")
        && (Tone is null or "dark" or "cheerful" or "hopeful" or "epic" or "mixed")
        && Text(Pronouns, 120) && Text(Motivation, 512) && Text(ImportantPerson, 512);
    private static bool Text(string? value, int maximum) => value is null
        || !string.IsNullOrWhiteSpace(value) && value == value.Trim()
            && value.Length <= maximum && !value.Any(char.IsControl);

    internal OriginChapterSource Apply(OriginChapterSource source)
    {
        if (!IsValid) throw new InvalidDataException("The opening story details are invalid.");
        if (IsEmpty) return source;
        // Distinct player-brief identity: never impersonate a Core accepted
        // decision or add these preferences to the canonical rules timeline.
        string digest = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(this))).ToLowerInvariant();
        string identity = "player-story-brief-" + digest;
        var copy = AndroidSurfaceStrings.Resolve(source.Locale);
        var lines = new List<string> { copy["Origin.SetupBrief"] };
        if (Gender is not null) lines.Add(copy["Origin.SetupGender"] + ": " + copy["Origin.Gender." + Gender]);
        if (Pronouns is not null) lines.Add(copy["Origin.SetupPronouns"] + ": " + Pronouns);
        if (Tone is not null) lines.Add(copy["Origin.SetupTone"] + ": " + copy["Origin.Tone." + Tone]);
        if (Motivation is not null) lines.Add(copy["Origin.SetupMotivation"] + ": " + Motivation);
        if (ImportantPerson is not null) lines.Add(copy["Origin.SetupPerson"] + ": " + ImportantPerson);
        return OriginChapterSourceIdentity.Capture(source with { Facts = source.Facts
            .Append(new OriginChapterSourceFact(identity, identity, string.Join("\n", lines))).ToArray() });
    }
}

internal sealed record OriginBookReadingChapter(string ChapterId, OriginBookProseDraft? Selected, OriginBookProseDraft? Pending);
internal sealed record OriginBookReadingState(string Owner, string Workspace, IReadOnlyList<OriginBookReadingChapter> Chapters)
{
    // Omission preserves historical file digests and old request identities.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OriginStoryProfile? StoryProfile { get; init; }
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
        => (state.StoryProfile is null || state.StoryProfile.IsValid && !state.StoryProfile.IsEmpty)
            && state.Chapters is { Count: <= 128 }
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
