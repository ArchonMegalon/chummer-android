using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    private const string PreferencePrefix = "chummer.android.rook-thread.v1.";
    private const int MaximumMessagesPerThread = 80;
    private const int MaximumPersistedMessageLength = 8_000;
    private readonly object _gate = new();
    private readonly Dictionary<string, Thread> _threads = new(StringComparer.Ordinal);

    public RookConversationThreadState Read(string workspaceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        lock (_gate)
        {
            Thread thread = GetOrRestoreThread(workspaceId);
            return thread.Snapshot();
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
            Thread thread = GetOrRestoreThread(snapshot.WorkspaceId);

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
            RookConversationThreadState state = thread.Snapshot();
            Persist(state);
            return state;
        }
    }

    private Thread GetOrRestoreThread(string workspaceId)
    {
        if (_threads.TryGetValue(workspaceId, out Thread? existing))
        {
            return existing;
        }

        string threadId = ThreadId(workspaceId);
        RookConversationThreadState? restored = Restore(workspaceId, threadId);
        var thread = new Thread(
            workspaceId,
            threadId,
            restored?.Messages ?? []);
        _threads.Add(workspaceId, thread);
        return thread;
    }

    private static RookConversationThreadState? Restore(string workspaceId, string threadId)
    {
        try
        {
            string json = Preferences.Default.Get(PreferenceKey(workspaceId), string.Empty);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            RookConversationThreadState? state = JsonSerializer.Deserialize<RookConversationThreadState>(json);
            if (state is null
                || !string.Equals(state.WorkspaceId, workspaceId, StringComparison.Ordinal)
                || !string.Equals(state.ThreadId, threadId, StringComparison.Ordinal)
                || state.Messages.Count > MaximumMessagesPerThread
                || state.Messages.Any(message => !IsValidPersistedMessage(message, threadId)))
            {
                return null;
            }

            return state;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }

    private static bool IsValidPersistedMessage(RookConversationMessage message, string threadId)
        => message.Role is RookConversationRoles.User or RookConversationRoles.Rook
            && message.MessageId.StartsWith($"{threadId}-", StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(message.Text)
            && message.Text.Length <= MaximumPersistedMessageLength
            && message.WorkspaceRevision > 0
            && IsSha256Digest(message.WizardSnapshotDigest);

    private static bool IsSha256Digest(string value)
        => value.Length == 71
            && value.StartsWith("sha256:", StringComparison.Ordinal)
            && value.AsSpan(7).IndexOfAnyExcept("0123456789abcdef") < 0;

    private static void Persist(RookConversationThreadState state)
    {
        try
        {
            Preferences.Default.Set(
                PreferenceKey(state.WorkspaceId),
                JsonSerializer.Serialize(state));
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException)
        {
            // Conversation persistence is best-effort. The grounded in-memory answer remains usable,
            // and no provider or mutation fallback is introduced when the platform store is unavailable.
        }
    }

    private static string PreferenceKey(string workspaceId)
        => PreferencePrefix + ThreadKey(workspaceId);

    private static string ThreadKey(string workspaceId)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(workspaceId));
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    private static string ThreadId(string workspaceId)
    {
        return $"rook-local-{ThreadKey(workspaceId)[..16]}";
    }

    private sealed class Thread
    {
        private readonly List<RookConversationMessage> _messages = [];
        private readonly string _workspaceId;
        private readonly string _threadId;
        private long _nextSequence;

        public Thread(
            string workspaceId,
            string threadId,
            IEnumerable<RookConversationMessage> messages)
        {
            _workspaceId = workspaceId;
            _threadId = threadId;
            _messages.AddRange(messages);
            _nextSequence = _messages
                .Select(message => ParseSequence(message.MessageId, threadId))
                .DefaultIfEmpty(0L)
                .Max();
        }

        public void Add(string role, string text, long revision, string snapshotDigest)
        {
            _messages.Add(new RookConversationMessage(
                $"{_threadId}-{checked(++_nextSequence)}",
                role,
                text,
                revision,
                snapshotDigest));
            if (_messages.Count > MaximumMessagesPerThread)
            {
                _messages.RemoveRange(0, _messages.Count - MaximumMessagesPerThread);
            }
        }

        public RookConversationThreadState Snapshot()
            => new(_workspaceId, _threadId, _messages.ToArray());

        private static long ParseSequence(string messageId, string threadId)
            => messageId.StartsWith($"{threadId}-", StringComparison.Ordinal)
               && long.TryParse(
                   messageId.AsSpan(threadId.Length + 1),
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out long value)
                ? value
                : 0L;
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
            && ContainsAny(normalized, "life module", "life-module", "module", "karma flow")
            && snapshot.Steps.FirstOrDefault(candidate => string.Equals(
                candidate.StepId,
                CharacterCreationWizardStepIds.LifeModules,
                StringComparison.Ordinal)) is { } lifeModuleStage
            && (!lifeModuleStage.IsAvailable || lifeModuleStage.Blockers.Count > 0))
        {
            return Prefix(
                "Life Modules are blocked by the current authoritative projection. Rook will not substitute "
                + "the Karma workflow. "
                + BlockerSentence(lifeModuleStage.Blockers));
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
