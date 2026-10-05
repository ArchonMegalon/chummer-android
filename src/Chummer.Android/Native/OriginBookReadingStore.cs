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
    // Omit new data on old profiles: their serialized bytes and paid request
    // identities must not change merely because the app was updated.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OriginStoryBackground? Background { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StoryLanguage { get; init; }
    internal bool IsEmpty => Gender is null && Pronouns is null && Tone is null
        && Motivation is null && ImportantPerson is null && (Background is null || Background.IsEmpty)
        && StoryLanguage is null;
    internal bool IsValid => (Gender is null or "male" or "female" or "other")
        && (Tone is null or "dark" or "cheerful" or "hopeful" or "epic" or "mixed")
        && (StoryLanguage is null or "de-DE" or "en-US" or "es-ES")
        && Text(Pronouns, 120) && Text(Motivation, 512) && Text(ImportantPerson, 512)
        && (Background is null || Background.IsValid && !Background.IsEmpty);
    internal static bool Text(string? value, int maximum) => value is null
        || !string.IsNullOrWhiteSpace(value) && value == value.Trim()
            && value.Length <= maximum && !value.Any(char.IsControl);

    internal OriginChapterSource Apply(OriginChapterSource source)
    {
        if (!IsValid) throw new InvalidDataException("The opening story details are invalid.");
        if (IsEmpty) return source;
        // Book output language is independent of the UI/Core decision locale.
        // It is bound into the authoring request, never a rewrite of rules or
        // previously retained prose. Null preserves historical request bytes.
        if (StoryLanguage is not null) source = source with { Locale = StoryLanguage };
        // Distinct player-brief identity: never impersonate a Core accepted
        // decision or add these preferences to the canonical rules timeline.
        var opening = this with { Background = null, StoryLanguage = null };
        string digest = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(opening))).ToLowerInvariant();
        string identity = "player-story-brief-" + digest;
        var copy = AndroidSurfaceStrings.Resolve(source.Locale);
        var lines = new List<string> { copy["Origin.SetupBrief"] };
        if (Gender is not null) lines.Add(copy["Origin.SetupGender"] + ": " + copy["Origin.Gender." + Gender]);
        if (Pronouns is not null) lines.Add(copy["Origin.SetupPronouns"] + ": " + Pronouns);
        if (Tone is not null) lines.Add(copy["Origin.SetupTone"] + ": " + copy["Origin.Tone." + Tone]);
        if (Motivation is not null) lines.Add(copy["Origin.SetupMotivation"] + ": " + Motivation);
        if (ImportantPerson is not null) lines.Add(copy["Origin.SetupPerson"] + ": " + ImportantPerson);
        var facts = source.Facts.ToList();
        if (!opening.IsEmpty) facts.Add(new(identity, identity, string.Join("\n", lines)));
        if (Background is { } background)
        {
            string backgroundId = "player-background-" + Convert.ToHexString(
                SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(background))).ToLowerInvariant();
            // Separate bounded narrative facts, never Core decision IDs. The
            // explicit chronology is part of the approved, frozen input. Do not
            // infer an age, diagnosis, abstinence or completed module from it.
            void Add(string suffix, string key, string? value, bool dated = false)
            {
                if (value is null) return;
                string text = copy["Origin.BackgroundBrief"] + "\n" + copy[key] + ": " + value;
                if (dated) text += "\n" + copy["Origin.BackgroundWhen"] + ": "
                    + copy["Origin.Period." + (background.Period ?? "unspecified")];
                facts.Add(new(backgroundId + "-" + suffix, backgroundId, text));
            }
            Add("family", "Origin.BackgroundFamily", background.BirthplaceFamily);
            Add("experiences", "Origin.BackgroundExperiences", background.Experiences, true);
            string? addiction = background.AddictionHistory;
            if (background.AddictionStatus is { } status)
                addiction = (addiction is null ? "" : addiction + "\n")
                    + copy["Origin.BackgroundStatus"] + ": " + copy["Origin.Addiction." + status];
            Add("addiction", "Origin.BackgroundAddiction", addiction, true);
            Add("turning-points", "Origin.BackgroundTurningPoints", background.TurningPoints, true);
            Add("anchors", "Origin.BackgroundAnchors", background.PositiveAnchors, true);
            // A chronology-only brief is meaningful too; don't silently drop it.
            if (background.Period is not null || background.Chronology is not null)
                Add("chronology", "Origin.BackgroundChronology", background.Chronology ?? copy["Origin.SetupUnspecified"], true);
        }
        return OriginChapterSourceIdentity.Capture(source with { Facts = facts.ToArray() });
    }
}

internal sealed record OriginStoryBackground(string? BirthplaceFamily = null, string? Experiences = null,
    string? AddictionHistory = null, string? AddictionStatus = null, string? TurningPoints = null,
    string? PositiveAnchors = null, string? Period = null, string? Chronology = null)
{
    internal bool IsEmpty => BirthplaceFamily is null && Experiences is null && AddictionHistory is null
        && AddictionStatus is null && TurningPoints is null && PositiveAnchors is null && Period is null && Chronology is null;
    internal bool IsValid => OriginStoryProfile.Text(BirthplaceFamily, 256)
        && OriginStoryProfile.Text(Experiences, 256) && OriginStoryProfile.Text(AddictionHistory, 256)
        && OriginStoryProfile.Text(TurningPoints, 256) && OriginStoryProfile.Text(PositiveAnchors, 256)
        && OriginStoryProfile.Text(Chronology, 256)
        && (AddictionStatus is null or "current" or "abstinent" or "recovery")
        && (Period is null or "childhood" or "teen" or "adult");
}

/// <summary>Player wishes for one selected module, never canonical rule facts.</summary>
internal sealed record OriginChapterRefinement(string Module, string? Motivation = null,
    string? Relationship = null, string? TurningPoint = null)
{
    internal bool IsEmpty => Motivation is null && Relationship is null && TurningPoint is null;
    internal bool IsValid => OriginStoryProfile.Text(Module, 160) && !string.IsNullOrWhiteSpace(Module)
        && OriginStoryProfile.Text(Motivation, 256) && OriginStoryProfile.Text(Relationship, 256)
        && OriginStoryProfile.Text(TurningPoint, 256);

    internal OriginChapterSource Apply(OriginChapterSource source)
    {
        if (!IsValid || IsEmpty) throw new InvalidDataException("Invalid chapter refinement.");
        var copy = AndroidSurfaceStrings.Resolve(source.Locale);
        var lines = new List<string> { copy["Origin.RefineBrief"], Module };
        if (Motivation is not null) lines.Add(copy.Format("Origin.RefineMotivation", Module) + " " + Motivation);
        if (Relationship is not null) lines.Add(copy.Format("Origin.RefineRelationship", Module) + " " + Relationship);
        if (TurningPoint is not null) lines.Add(copy.Format("Origin.RefineTurningPoint", Module) + " " + TurningPoint);
        string identity = "player-chapter-brief-" + Convert.ToHexString(
            SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(this))).ToLowerInvariant();
        return OriginChapterSourceIdentity.Capture(source with {
            Facts = [.. source.Facts, new(identity, identity, string.Join("\n", lines))] });
    }
}

internal sealed record OriginPendingChapterRefinement(string PreviewDigest, string TurnSeedDigest,
    string ChoiceId, int PreviousDecisionCount, OriginChapterRefinement Brief);
internal sealed record OriginBoundChapterRefinement(string ChapterId, string AcceptedDecisionId,
    OriginChapterRefinement Brief);

internal sealed record OriginBookReadingChapter(string ChapterId, OriginBookProseDraft? Selected, OriginBookProseDraft? Pending)
{
    // Freeze exactly what was approved, before HTTP dispatch. Optional so old
    // editions retain their bytes/digests until an actual source is retained.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OriginChapterSource? AuthoringSource { get; init; }
}
internal sealed record OriginBookReadingState(string Owner, string Workspace, IReadOnlyList<OriginBookReadingChapter> Chapters)
{
    // Omission preserves historical file digests and old request identities.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OriginStoryProfile? StoryProfile { get; init; }
    // Only the updated illustrated-book consent grants this scope. Old saved
    // editions omit it and keep their exact bytes/digests and provider consent.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IllustrationPolicy { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OriginPendingChapterRefinement? PendingRefinement { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<OriginBoundChapterRefinement>? ChapterRefinements { get; init; }
    internal const string AutomaticIllustrations = "automatic-private-book/v1";
    internal string Digest
    {
        get
        {
            // Preserve the v1 JSON bytes/digest without allocating another
            // complete UTF-8 copy of every selected and pending chapter.
            using var hash = SHA256.Create();
            using var hashing = new CryptoStream(Stream.Null, hash, CryptoStreamMode.Write);
            using var bounded = new OriginBookReadingStore.BoundedEditionStream(hashing);
            JsonSerializer.Serialize(bounded, this);
            hashing.FlushFinalBlock();
            return Convert.ToHexString(hash.Hash!).ToLowerInvariant();
        }
    }
}

/// <summary>
/// App-private reading editions only. Called synchronously on a worker while
/// holding the captured owner lease. No workspace document or rules writes.
/// </summary>
public sealed class OriginBookReadingStore(string stateDirectory)
{
    internal const int MaximumChapters = 128;
    // 128 chapters x selected/pending x 64 KiB text x up to 6 JSON-escape
    // bytes = 96 MiB, plus frozen (<=32 KiB each) inputs and metadata. This
    // envelope admits the existing per-chapter contract, not unbounded books.
    private const int MaximumBytes = 128 * 1024 * 1024;
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
        using var bounded = new BoundedEditionStream(file);
        var saved = JsonSerializer.Deserialize<FileState>(bounded);
        if (saved?.Schema != Schema || saved.State is not { } state || state.Owner != owner || state.Workspace != workspace
            || !Valid(state) || saved.Digest != state.Digest)
            throw new InvalidDataException("The reading edition has an invalid identity or content binding.");
        return state;
    }

    internal OriginBookReadingState Save(OriginBookReadingState expected, OriginBookReadingState next,
        Func<bool> stillCurrent, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (next.Owner != expected.Owner || next.Workspace != expected.Workspace || !Valid(next))
            throw new InvalidDataException("The reading edition changed identity.");
        if (expected.Chapters.Count != 0 && next.StoryProfile != expected.StoryProfile)
            throw new InvalidOperationException("The story brief is frozen once a chapter is retained.");
        // A confirmed chapter brief is append-only, including before HTTP has
        // started. Editing it later would silently create a different paid job.
        if (expected.ChapterRefinements?.Any(old =>
            next.ChapterRefinements?.Contains(old) != true) == true)
            throw new InvalidOperationException("Confirmed chapter refinements are frozen.");
        if (next.ChapterRefinements?.Any(added => expected.ChapterRefinements?.Contains(added) != true
            && expected.Chapters.Any(c => c.ChapterId == added.ChapterId)) == true)
            throw new InvalidOperationException("A retained chapter cannot receive a new refinement.");
        var saved = new FileState(Schema, next, next.Digest);
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
                {
                    using var bounded = new BoundedEditionStream(output, cancellationToken: ct);
                    JsonSerializer.Serialize(bounded, saved);
                    output.Flush(flushToDisk: true);
                }
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

    // Sequential JSON I/O only; the caller owns/disposes the underlying stream.
    // Check the bytes actually read as well as FileStream.Length so even a
    // growing input cannot evade the cap. Oversized writes never commit.
    internal sealed class BoundedEditionStream(Stream inner, long maximumBytes = MaximumBytes,
        CancellationToken cancellationToken = default) : Stream
    {
        private long _used;
        public override bool CanRead => inner.CanRead;
        public override bool CanWrite => inner.CanWrite;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = inner.Read(buffer[..(int)Math.Min(buffer.Length, maximumBytes - _used + 1)]);
            _used += read;
            if (_used > maximumBytes) throw new InvalidDataException("The reading edition is oversized.");
            return read;
        }
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (buffer.Length > maximumBytes - _used) throw new InvalidDataException("The reading edition is oversized.");
            inner.Write(buffer);
            _used += buffer.Length;
        }
    }

    private static bool Valid(OriginBookReadingState state)
        => (state.StoryProfile is null || state.StoryProfile.IsValid && !state.StoryProfile.IsEmpty)
            && (state.IllustrationPolicy is null or OriginBookReadingState.AutomaticIllustrations)
            && state.Chapters is { Count: <= MaximumChapters }
            && ValidRefinements(state)
            && state.Chapters.All(c => c is not null)
            && state.Chapters.Select(c => c.ChapterId).Distinct(StringComparer.Ordinal).Count() == state.Chapters.Count
            && state.Chapters.All(c => !string.IsNullOrWhiteSpace(c.ChapterId)
                && (c.Selected is null || c.Selected.IsValid() && c.Selected.ChapterId == c.ChapterId)
                && (c.Pending is null || c.Pending.IsValid() && c.Pending.ChapterId == c.ChapterId)
                && ValidSource(state, c));

    private static bool ValidRefinements(OriginBookReadingState state)
    {
        bool Id(string? value) => value is not null && OriginStoryProfile.Text(value, 256);
        bool Digest(string? value) => value is { Length: 64 }
            && value.All(c => c is >= 'a' and <= 'f' or >= '0' and <= '9');
        return (state.PendingRefinement is null || state.PendingRefinement is { } pending
            && Digest(pending.PreviewDigest) && Digest(pending.TurnSeedDigest) && Id(pending.ChoiceId)
            && pending.PreviousDecisionCount is >= 0 and < MaximumChapters
            && pending.Brief is { IsValid: true, IsEmpty: false })
            && (state.ChapterRefinements is null || state.ChapterRefinements is { Count: > 0 and <= MaximumChapters } refinements
                && refinements.All(r => r is not null && Id(r.ChapterId) && Id(r.AcceptedDecisionId)
                    && r.Brief is { IsValid: true, IsEmpty: false })
                && refinements.Select(r => r.ChapterId).Distinct(StringComparer.Ordinal).Count() == refinements.Count
                && refinements.Select(r => r.AcceptedDecisionId).Distinct(StringComparer.Ordinal).Count() == refinements.Count);
    }

    internal OriginBookReadingState BindRefinement(OriginBookReadingState expected,
        Chummer.Contracts.LifeModules.LifeModuleOriginDossierDraftCheckpoint before,
        Chummer.Contracts.LifeModules.LifeModuleOriginDossierInteractionAdvance advance,
        Func<bool> stillCurrent)
    {
        if (expected.PendingRefinement is not { } pending) return expected;
        var receipt = advance.AcceptedDecision;
        var projection = advance.Checkpoint.Projection;
        if (expected.Owner != before.OwnerId || expected.Workspace != before.WorkspaceId
            || advance.Checkpoint.OwnerId != before.OwnerId || advance.Checkpoint.WorkspaceId != before.WorkspaceId
            || pending.PreviewDigest != before.PendingPreview?.PreviewDigest
            || pending.TurnSeedDigest != before.Projection.CurrentTurn.SeedDigest
            || pending.ChoiceId != receipt.ChoiceId
            || pending.PreviousDecisionCount != before.Projection.CanonicalLayer.AcceptedDecisionIds.Count
            || projection.CurrentTurn.PreviousTurnDigest != pending.TurnSeedDigest
            || !projection.CanonicalLayer.AcceptedDecisionIds.SequenceEqual(
                before.Projection.CanonicalLayer.AcceptedDecisionIds.Append(receipt.DecisionId)))
            throw new InvalidOperationException("The chapter refinement belongs to another decision.");
        var chapter = projection.VisibleChapters.Single(c => c.ThroughAcceptedDecisionId == receipt.DecisionId);
        if (expected.Chapters.Any(c => c.ChapterId == chapter.ChapterId))
            throw new InvalidOperationException("This chapter has already been requested.");
        var bound = new OriginBoundChapterRefinement(chapter.ChapterId, receipt.DecisionId, pending.Brief);
        return Save(expected, expected with { PendingRefinement = null,
            ChapterRefinements = [.. expected.ChapterRefinements ?? [], bound] }, stillCurrent, CancellationToken.None);
    }

    private static bool ValidSource(OriginBookReadingState state, OriginBookReadingChapter chapter)
    {
        if (chapter.AuthoringSource is not { } source) return true;
        try
        {
            string request = OriginChapterSourceIdentity.RequestId(source);
            return source.WorkspaceId == state.Workspace && source.ChapterId == chapter.ChapterId
                && (chapter.Selected is null || chapter.Selected.JobId == request)
                && (chapter.Pending is null || chapter.Pending.JobId == request);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException) { return false; }
    }

    private string PathFor(string owner, string workspace)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner); ArgumentException.ThrowIfNullOrWhiteSpace(workspace);
        return Path.Combine(_root, Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(owner + "\0" + workspace))) + ".json");
    }
}
