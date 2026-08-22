using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Chummer.Contracts.Characters;

namespace Chummer.Android.Native;

public static class RookConversationRoles
{
    public const string User = "user";
    public const string Rook = "rook";
}

public sealed record RookConversationMessage(
    string MessageId,
    string Role,
    string Text,
    long WorkspaceRevision,
    string WizardSnapshotDigest)
{
    public bool IsStale(long currentRevision, string? currentSnapshotDigest)
        => WorkspaceRevision != currentRevision
            || !string.Equals(WizardSnapshotDigest, currentSnapshotDigest, StringComparison.Ordinal);
}

public sealed record RookConversationThreadState(
    string WorkspaceId,
    string ThreadId,
    IReadOnlyList<RookConversationMessage> Messages)
{
    public static RookConversationThreadState Empty { get; } = new(string.Empty, string.Empty, []);
}

/// <summary>
/// Phone-session conversation memory. Threads are isolated by workspace and every message carries
/// the exact revision/snapshot binding that authorized its facts. No provider or mutation path is
/// reachable from this store.
/// </summary>
public sealed class RookConversationStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Thread> _threads = new(StringComparer.Ordinal);

    public RookConversationThreadState Read(string workspaceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        lock (_gate)
        {
            return _threads.TryGetValue(workspaceId, out Thread? thread)
                ? thread.Snapshot()
                : new RookConversationThreadState(workspaceId, ThreadId(workspaceId), []);
        }
    }

    public RookConversationThreadState AddGroundedTurn(
        CharacterCreationWizardSnapshot snapshot,
        string question)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        string normalizedQuestion = question.Trim();
        if (normalizedQuestion.Length > 2000)
        {
            normalizedQuestion = normalizedQuestion[..2000];
        }

        lock (_gate)
        {
            if (!_threads.TryGetValue(snapshot.WorkspaceId, out Thread? thread))
            {
                thread = new Thread(snapshot.WorkspaceId, ThreadId(snapshot.WorkspaceId));
                _threads.Add(snapshot.WorkspaceId, thread);
            }

            thread.Add(
                RookConversationRoles.User,
                normalizedQuestion,
                snapshot.WorkspaceRevision,
                snapshot.SnapshotDigest);
            thread.Add(
                RookConversationRoles.Rook,
                RookLocalGroundedResponder.Answer(snapshot, normalizedQuestion),
                snapshot.WorkspaceRevision,
                snapshot.SnapshotDigest);
            return thread.Snapshot();
        }
    }

    private static string ThreadId(string workspaceId)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(workspaceId));
        return $"rook-local-{Convert.ToHexString(digest)[..16].ToLowerInvariant()}";
    }

    private sealed class Thread(string workspaceId, string threadId)
    {
        private readonly List<RookConversationMessage> _messages = [];

        public void Add(string role, string text, long revision, string snapshotDigest)
            => _messages.Add(new RookConversationMessage(
                $"{threadId}-{_messages.Count + 1}",
                role,
                text,
                revision,
                snapshotDigest));

        public RookConversationThreadState Snapshot()
            => new(workspaceId, threadId, _messages.ToArray());
    }
}

public static class RookLocalGroundedResponder
{
    public const string FallbackLabel = "Local grounded fallback";

    public static string Answer(CharacterCreationWizardSnapshot snapshot, string question)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        string normalized = question.Trim().ToLowerInvariant();

        if (string.Equals(snapshot.BuildMethod, CharacterCreationBuildMethods.LifeModules, StringComparison.Ordinal)
            && ContainsAny(normalized, "life module", "life-module", "module", "karma flow"))
        {
            return Prefix(
                "Life Modules stay fail-closed on this phone foundation. Rook will not substitute "
                + "the Karma workflow. "
                + BlockerSentence(snapshot.CompletionBlockers));
        }

        CharacterCreationBudgetState? budget = snapshot.Budgets.FirstOrDefault(candidate =>
            Mentions(normalized, candidate.BudgetId) || Mentions(normalized, candidate.Label));
        if (budget is not null)
        {
            return Prefix(BudgetSentence(budget));
        }

        CharacterCreationLegalOption? option = snapshot.LegalOptionsByStep.Values
            .SelectMany(static options => options)
            .FirstOrDefault(candidate =>
                Mentions(normalized, candidate.OptionId) || Mentions(normalized, candidate.Label));
        if (option is not null)
        {
            string availability = option.IsEnabled
                ? "It is currently legal."
                : $"It is currently disabled: {DisableReason(option)}.";
            string costs = option.Costs.Count == 0
                ? "No projected budget cost is attached."
                : "Projected costs: " + string.Join(
                    ", ",
                    option.Costs.Select(cost =>
                        $"{Format(cost.Delta)} {cost.Unit} from {Humanize(cost.BudgetId)}")) + ".";
            return Prefix($"{option.Label}: {availability} {costs}");
        }

        CharacterCreationWizardStageState? stage = snapshot.Steps.FirstOrDefault(candidate =>
            Mentions(normalized, candidate.StepId) || Mentions(normalized, candidate.Label));
        if (stage is not null)
        {
            string next = stage.LegalNextStepIds.Count == 0
                ? "No legal next stage is projected from it."
                : $"Legal next stages: {Labels(snapshot, stage.LegalNextStepIds)}.";
            return Prefix(
                $"{stage.Label} is {Humanize(stage.Status)}. "
                + BlockerSentence(stage.Blockers)
                + " "
                + next
                + " "
                + LegalOptionsSentence(snapshot, stage.StepId)
                + " "
                + TipSentence(snapshot, stage));
        }

        if (ContainsAny(normalized, "block", "why", "finish", "complete", "legal"))
        {
            return Prefix(BlockerSentence(snapshot.CompletionBlockers));
        }

        CharacterCreationWizardStageState? active = snapshot.Steps.FirstOrDefault(candidate =>
            string.Equals(candidate.StepId, snapshot.ActiveStepId, StringComparison.Ordinal));
        string activeFact = active is null
            ? $"The projected active stage is {Humanize(snapshot.ActiveStepId)}."
            : $"The active stage is {active.Label} ({Humanize(active.Status)}).";
        string budgets = snapshot.Budgets.Count == 0
            ? "No authoritative budgets are projected."
            : string.Join(" ", snapshot.Budgets.Select(BudgetSentence));
        string nextSteps = active?.LegalNextStepIds.Count > 0
            ? $"Legal next stages: {Labels(snapshot, active.LegalNextStepIds)}."
            : "No legal next stage is currently projected.";
        string legalOptions = active is null
            ? "No active-stage legal options are projected."
            : LegalOptionsSentence(snapshot, active.StepId);
        return Prefix(
            $"{activeFact} {budgets} {nextSteps} {legalOptions} "
            + BlockerSentence(snapshot.CompletionBlockers)
            + " "
            + TipSentence(snapshot, active));
    }

    private static string BudgetSentence(CharacterCreationBudgetState budget)
    {
        if (!budget.IsExact)
        {
            return $"{budget.Label} is not exact; {BlockerSentence(budget.Blockers)}";
        }

        string unit = string.IsNullOrWhiteSpace(budget.Unit) ? "points" : budget.Unit;
        return $"{budget.Label}: {Format(budget.Remaining)} {unit} remaining "
            + $"({Format(budget.Used)} used of {Format(budget.Total)}).";
    }

    private static string BlockerSentence(IReadOnlyList<string> blockers)
        => blockers.Count == 0
            ? "No authoritative blocker is reported."
            : $"Current blockers: {string.Join("; ", blockers)}.";

    private static string Labels(CharacterCreationWizardSnapshot snapshot, IReadOnlyList<string> ids)
        => string.Join(", ", ids.Select(id =>
            snapshot.Steps.FirstOrDefault(step => string.Equals(step.StepId, id, StringComparison.Ordinal))?.Label
            ?? Humanize(id)));

    private static string LegalOptionsSentence(CharacterCreationWizardSnapshot snapshot, string stepId)
    {
        if (!snapshot.LegalOptionsByStep.TryGetValue(stepId, out IReadOnlyList<CharacterCreationLegalOption>? options)
            || options.Count == 0)
        {
            return "No authoritative legal options are projected for this stage.";
        }

        CharacterCreationLegalOption[] enabled = options.Where(static option => option.IsEnabled).Take(8).ToArray();
        if (enabled.Length == 0)
        {
            return "No option is currently enabled for this stage.";
        }

        string suffix = options.Count(option => option.IsEnabled) > enabled.Length ? ", …" : string.Empty;
        return $"Enabled options: {string.Join(", ", enabled.Select(static option => option.Label))}{suffix}.";
    }

    private static string DisableReason(CharacterCreationLegalOption option)
    {
        if (string.IsNullOrWhiteSpace(option.DisableReasonKey))
        {
            return "the local rules projection did not provide a reason";
        }

        if (option.DisableReasonArguments.Count == 0)
        {
            return Humanize(option.DisableReasonKey);
        }

        return Humanize(option.DisableReasonKey)
            + " ("
            + string.Join(", ", option.DisableReasonArguments.Select(static pair => $"{pair.Key}={pair.Value}"))
            + ")";
    }

    private static string TipSentence(
        CharacterCreationWizardSnapshot snapshot,
        CharacterCreationWizardStageState? stage)
    {
        if (stage is not null && stage.Blockers.Count > 0)
        {
            return $"Grounded tip: resolve “{stage.Blockers[0]}” before trying to advance this stage.";
        }

        if (stage is not null
            && snapshot.LegalOptionsByStep.TryGetValue(
                stage.StepId,
                out IReadOnlyList<CharacterCreationLegalOption>? options)
            && options.FirstOrDefault(static option => option.IsEnabled) is { } enabled)
        {
            return $"Grounded tip: {enabled.Label} is an enabled option you can inspect next.";
        }

        CharacterCreationBudgetState? remaining = snapshot.Budgets.FirstOrDefault(budget =>
            budget.IsExact && budget.Remaining > 0);
        if (remaining is not null)
        {
            return $"Grounded tip: {remaining.Label} still has {Format(remaining.Remaining)} "
                + $"{remaining.Unit} available.";
        }

        return "Grounded tip: review the projected blockers before continuing.";
    }

    private static bool Mentions(string question, string value)
    {
        string normalizedValue = value.Trim().ToLowerInvariant();
        return normalizedValue.Length > 0
            && (question.Contains(normalizedValue, StringComparison.Ordinal)
                || question.Contains(normalizedValue.Replace('-', ' '), StringComparison.Ordinal));
    }

    private static bool ContainsAny(string value, params string[] candidates)
        => candidates.Any(candidate => value.Contains(candidate, StringComparison.Ordinal));

    private static string Prefix(string text) => $"{FallbackLabel}. {text}";

    private static string Humanize(string value)
        => RunnerSessionCoordinator.HumanizeId(value);

    private static string Format(decimal value)
        => value.ToString("0.##", CultureInfo.InvariantCulture);
}
