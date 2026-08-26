namespace Chummer.Contracts.Workspaces
{
    public readonly record struct CharacterWorkspaceId(string Value);
}

namespace Chummer.Contracts.Characters
{
    public static class CharacterCreationBuildMethods
    {
        public const string Priority = "Priority";
    }

    public static class CharacterCreationFoundationOutcomes
    {
        public const string Success = "success";
        public const string Blocked = "blocked";
        public const string Conflict = "conflict";
    }

    public sealed record CharacterCreationFoundationResult<T>(
        string Outcome,
        T? Value,
        IReadOnlyList<string> Blockers)
        where T : class;

    public sealed record CharacterCreationPrerequisiteDraft(
        long DraftRevision,
        string DraftDigest);

    public sealed record CharacterCreationAttributesDraft(
        long DraftRevision,
        string DraftDigest);
}

namespace Chummer.Presentation.Overview
{
    using Chummer.Contracts.Workspaces;

    public sealed record CharacterOverviewProfileStub(bool Created);

    public sealed class CharacterOverviewState
    {
        public CharacterOverviewProfileStub? Profile { get; init; }
        public CharacterWorkspaceId? WorkspaceId { get; init; }
        public long ContentRevision { get; init; }
        public long SavedRevision { get; init; }
        public bool IsDirty { get; init; }
        public string? Error { get; init; }
    }
}

namespace Microsoft.Maui.Storage
{
    public interface IPreferences
    {
        string Get(string key, string fallback);
        void Set(string key, string value);
        void Remove(string key);
    }

    internal sealed class MemoryPreferences : IPreferences
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public string Get(string key, string fallback)
            => _values.TryGetValue(key, out string? value) ? value : fallback;

        public void Set(string key, string value) => _values[key] = value;
        public void Remove(string key) => _values.Remove(key);
    }

    public static class Preferences
    {
        public static IPreferences Default { get; } = new MemoryPreferences();
    }
}
