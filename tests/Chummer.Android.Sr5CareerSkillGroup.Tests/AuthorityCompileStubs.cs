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

    public sealed record CareerSkillGroupAdvanceEditorState(
        CharacterWorkspaceId WorkspaceId,
        long ContentRevision,
        string RulesetId,
        IReadOnlyList<CharacterCareerSkillGroupAdvanceQuote> SkillGroups,
        int OmittedSkillGroupCount,
        IReadOnlyList<CharacterCareerSkillGroupAdvanceReceipt> RecoverableReceipts,
        int OmittedReceiptCount);

    public sealed record CareerSkillGroupAdvanceRequest(
        CharacterWorkspaceId WorkspaceId,
        long ExpectedContentRevision,
        string ExpectedRulesetId,
        CharacterCareerSkillGroupAdvanceQuote ExpectedSkillGroup,
        string ExpectedLogicalRevision,
        string ExpectedSourceRevision,
        string ExpectedRuleDigest,
        bool Confirmed,
        Guid ExpenseId,
        DateTime ExpenseDateLocal);

    public sealed record CareerSkillGroupCorrectionRequest(
        CharacterWorkspaceId WorkspaceId,
        long ExpectedContentRevision,
        string ExpectedRulesetId,
        CharacterCareerSkillGroupAdvanceReceipt OriginalReceipt,
        string ExpectedReceiptDigest,
        bool Confirmed,
        Guid CorrectionId,
        string Reason);
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

    public sealed record Sr5CareerRunnerBinding(
        bool Created,
        string? GameEdition,
        CharacterWorkspaceId? WorkspaceId,
        long ContentRevision,
        long SavedRevision,
        bool IsDirty,
        string? Error);

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

        public Task<Chummer.Presentation.Overview.CareerSkillGroupAdvanceEditorState?>
            PrepareCareerSkillGroupAdvanceAsync(CancellationToken cancellationToken)
            => Task.FromResult<Chummer.Presentation.Overview.CareerSkillGroupAdvanceEditorState?>(null);

        public Task<bool> ApplyCareerSkillGroupAdvanceAsync(
            Chummer.Presentation.Overview.CareerSkillGroupAdvanceRequest request,
            CancellationToken cancellationToken)
            => Task.FromResult(false);

        public Task<CharacterCareerSkillGroupCorrectionPlan?> CorrectCareerSkillGroupAdvanceAsync(
            Chummer.Presentation.Overview.CareerSkillGroupCorrectionRequest request,
            CancellationToken cancellationToken)
            => Task.FromResult<CharacterCareerSkillGroupCorrectionPlan?>(null);
    }

    public static class Sr5CareerActiveSkillCoordinator
    {
        public static void RequireCreatedSr5(Sr5CareerRunnerBinding binding)
        {
            ArgumentNullException.ThrowIfNull(binding);
            if (!Sr5CareerWizardCatalog.IsSr5CareerRunner(binding.Created, binding.GameEdition)
                || binding.WorkspaceId is not { } workspaceId
                || string.IsNullOrWhiteSpace(workspaceId.Value)
                || binding.ContentRevision <= 0)
            {
                throw new InvalidOperationException("A created exact SR5 runner is required.");
            }
        }
    }
}
