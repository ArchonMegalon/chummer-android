using Chummer.Contracts.Characters;

namespace Chummer.Contracts.Workspaces
{
    public readonly record struct CharacterWorkspaceId(string Value);
}

namespace Chummer.Presentation.Overview
{
    using Chummer.Contracts.Workspaces;

    public sealed record CareerActiveSkillAdvanceEditorState(
        CharacterWorkspaceId WorkspaceId,
        long ContentRevision,
        IReadOnlyList<CharacterCareerActiveSkillAdvanceQuote> Skills,
        int OmittedSkillCount);

    public sealed record CareerActiveSkillAdvanceRequest(
        CharacterWorkspaceId WorkspaceId,
        long ExpectedContentRevision,
        CharacterCareerActiveSkillAdvanceQuote ExpectedSkill,
        string ExpectedRuleDigest,
        bool Confirmed,
        Guid ExpenseId,
        DateTime ExpenseDateLocal);

    public sealed record CareerAttributeAdvanceEditorState(
        CharacterWorkspaceId WorkspaceId,
        long ContentRevision,
        IReadOnlyList<CharacterCareerAttributeAdvanceQuote> Attributes,
        int OmittedAttributeCount,
        IReadOnlyList<CharacterCareerAttributeAdvanceReceipt> RecoverableReceipts,
        int OmittedReceiptCount);

    public sealed record CareerAttributeAdvanceRequest(
        CharacterWorkspaceId WorkspaceId,
        long ExpectedContentRevision,
        CharacterCareerAttributeAdvanceQuote ExpectedAttribute,
        string ExpectedLogicalRevision,
        string ExpectedSourceRevision,
        string ExpectedRuleDigest,
        bool Confirmed,
        Guid ExpenseId,
        DateTime ExpenseDateLocal);
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

namespace Chummer.Android.Native
{
    using Chummer.Contracts.Workspaces;

    public static class Sr5CareerWizardPage
    {
        public static string LaneToken(Sr5CareerWizardLane lane)
            => lane.ToString().ToLowerInvariant();
    }

    public interface ISr5CareerCheckpointBackend
    {
        string Read();
        void Write(string payload);
        void Remove();
    }

    public interface ISr5CareerCheckpointOwnerAuthority
    {
        Guid CurrentOwnerId { get; }
    }

    public sealed record RunnerSessionProfileStub(bool Created);
    public sealed record RunnerSessionRulesStub(string? GameEdition);

    public sealed class RunnerSessionStateStub
    {
        public RunnerSessionProfileStub? Profile { get; init; }
        public RunnerSessionRulesStub? Rules { get; init; }
        public CharacterWorkspaceId? WorkspaceId { get; init; }
        public long ContentRevision { get; init; }
        public long SavedRevision { get; init; }
        public bool IsDirty { get; init; }
        public string? Error { get; init; }
    }

    public sealed class RunnerSessionCoordinator
    {
        public RunnerSessionStateStub State { get; } = new();

        public Task<Chummer.Presentation.Overview.CareerAttributeAdvanceEditorState?>
            PrepareCareerAttributeAdvanceAsync(CancellationToken cancellationToken)
            => Task.FromResult<Chummer.Presentation.Overview.CareerAttributeAdvanceEditorState?>(null);

        public Task<bool> ApplyCareerAttributeAdvanceAsync(
            Chummer.Presentation.Overview.CareerAttributeAdvanceRequest request,
            CancellationToken cancellationToken)
            => Task.FromResult(false);
    }

}
