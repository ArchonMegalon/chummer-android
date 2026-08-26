using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Chummer.Contracts.Characters;

namespace Chummer.Contracts.Workspaces
{
    public readonly record struct CharacterWorkspaceId(string Value);
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

    public enum Sr5CareerWizardLane
    {
        Advancement,
        BeforeRun,
        LiveRun,
        AfterRun,
        Downtime,
        Corrections
    }

    public static class Sr5CareerWizardRoutes
    {
        public const string AfterRunChoose = "sr5-career/after-run/settlement/choose";
        public const string AfterRunRewards = "sr5-career/after-run/settlement/rewards";
        public const string AfterRunConsequences = "sr5-career/after-run/settlement/consequences";
        public const string AfterRunContacts = "sr5-career/after-run/settlement/contacts";
        public const string AfterRunGmReview = "sr5-career/after-run/settlement/gm-review";
        public const string AfterRunOwnerReview = "sr5-career/after-run/settlement/owner-review";
        public const string AfterRunReview = "sr5-career/after-run/settlement/review";
        public const string AfterRunReceipt = "sr5-career/after-run/settlement/receipt";
    }

    public static class Sr5CareerWizardCatalog
    {
        public static bool IsSr5CareerRunner(bool created, string? gameEdition)
            => created && string.Equals(gameEdition, "SR5", StringComparison.OrdinalIgnoreCase);
    }

    public sealed record Sr5CareerRunnerBinding(
        bool Created,
        string? GameEdition,
        CharacterWorkspaceId? WorkspaceId,
        long ContentRevision,
        long SavedRevision,
        bool IsDirty,
        string? Error);

    public static class Sr5CareerRunnerGuard
    {
        public static void RequireCreated(Sr5CareerRunnerBinding binding)
        {
            if (!Sr5CareerWizardCatalog.IsSr5CareerRunner(binding.Created, binding.GameEdition)
                || binding.WorkspaceId is not { } workspaceId
                || string.IsNullOrWhiteSpace(workspaceId.Value)
                || binding.ContentRevision <= 0)
            {
                throw new InvalidOperationException("Created SR5 runner required.");
            }
        }
    }

    public enum Sr5CareerCheckpointPhase
    {
        Reviewed,
        Applying,
        Applied
    }

    public enum Sr5CareerActionKind
    {
        AfterRunSettlement = 6
    }

    public sealed record Sr5CareerCostQuote(
        int KarmaCost,
        decimal NuyenCost,
        decimal EssenceCost,
        int? Availability,
        TimeSpan? ElapsedTime,
        string RuleDigest,
        string LogicalRevision,
        bool IsExact,
        string Blocker);

    public sealed record Sr5CareerActionPlan(
        Guid OwnerId,
        Guid ActionId,
        string IdempotencyKey,
        string RouteId,
        Sr5CareerActionKind Kind,
        CharacterWorkspaceId WorkspaceId,
        long ExpectedContentRevision,
        string DomainIdentity,
        Sr5CareerCostQuote CostQuote)
    {
        public static Sr5CareerActionPlan FromAfterRunSettlement(
            Guid ownerId,
            CharacterAfterRunSettlementQuoteBinding binding,
            CharacterAfterRunSettlementPlan plan,
            string rewardContextDigest)
        {
            string identity = string.Join(
                ":",
                binding.Identity.ProposalId.ToString("D"),
                binding.Identity.RunId.ToString("D"),
                binding.Identity.CharacterId.ToString("D"));
            string payload = string.Join(
                '\0',
                ownerId.ToString("D"),
                binding.WorkspaceId.Value,
                binding.WorkspaceRevision.ToString(CultureInfo.InvariantCulture),
                binding.BindingDigest,
                rewardContextDigest,
                plan.PlanDigest,
                plan.TransactionId.ToString("D"));
            string digest = Convert.ToHexStringLower(
                SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
            return new(
                ownerId,
                plan.TransactionId,
                digest,
                Sr5CareerWizardRoutes.AfterRunReview,
                Sr5CareerActionKind.AfterRunSettlement,
                binding.WorkspaceId,
                binding.WorkspaceRevision,
                identity,
                new(
                    binding.Quote.ContactKarmaCost,
                    0m,
                    0m,
                    null,
                    null,
                    binding.Quote.GmPolicyDigest,
                    binding.Quote.LogicalDigest,
                    CharacterAfterRunSettlementRules.IsCoherent(binding.Quote)
                        && CharacterAfterRunSettlementRules.IsCoherent(plan),
                    binding.Quote.CanSettle ? string.Empty : binding.Quote.Blocker.ToString()));
        }
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

        public Task<Sr5AfterRunSettlementEditorState> PrepareAfterRunSettlementAsync(
            CancellationToken cancellationToken)
            => Task.FromResult(Sr5AfterRunSettlementEditorState.Unavailable(
                new CharacterWorkspaceId("workspace-stub"),
                1,
                "unavailable"));

        public Task<CharacterAfterRunSettlementResult?> SettleAfterRunAsync(
            CharacterAfterRunSettlementCommand command,
            CancellationToken cancellationToken)
            => Task.FromResult<CharacterAfterRunSettlementResult?>(null);
    }
}
