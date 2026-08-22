using Chummer.Contracts.Characters;

namespace Chummer.Android.Native;

public sealed class RookConversationPage : NativePageBase
{
    private readonly VerticalStackLayout _messages = new()
    {
        Padding = new Thickness(20, 18, 20, 28),
        Spacing = 12
    };
    private readonly Editor _question = NativeTheme.TextArea(
        "rook-question",
        string.Empty,
        "Ask about this step, your budgets, blockers, or legal options…");
    private readonly Button _send = NativeTheme.PrimaryButton("Ask Rook");

    public RookConversationPage(RunnerSessionCoordinator coordinator) : base(coordinator)
    {
        Title = "Rook";
        AutomationId = "creation-rook-conversation";
        _question.MinimumHeightRequest = 76;
        _send.AutomationId = "rook-send-question";
        _send.Clicked += async (_, _) => await RunAsync(() =>
        {
            Coordinator.AskRook(_question.Text ?? string.Empty);
            _question.Text = string.Empty;
            return Task.CompletedTask;
        });

        VerticalStackLayout composer = new()
        {
            Padding = new Thickness(16, 10, 16, 18),
            Spacing = 8,
            BackgroundColor = NativeTheme.Surface,
            Children =
            {
                NativeTheme.FieldLabel("Follow-up question"),
                _question,
                _send
            }
        };
        Grid layout = new()
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto)
            }
        };
        layout.Add(new ScrollView { Content = _messages });
        layout.Add(composer, 0, 1);
        Content = layout;
    }

    protected override void Refresh()
    {
        _messages.Clear();
        CharacterCreationWizardSnapshot? snapshot = Coordinator.State.CreationWizard;
        bool hasCurrentBinding = snapshot is not null
            && Coordinator.State.WorkspaceId is { } workspaceId
            && string.Equals(snapshot.WorkspaceId, workspaceId.Value, StringComparison.Ordinal)
            && snapshot.WorkspaceRevision == Coordinator.State.ContentRevision;
        _send.IsEnabled = hasCurrentBinding && !string.IsNullOrWhiteSpace(_question.Text);
        _question.TextChanged -= QuestionTextChanged;
        _question.TextChanged += QuestionTextChanged;

        _messages.Add(NativeTheme.Eyebrow("Build companion"));
        _messages.Add(NativeTheme.Title("Ask Rook", 25));
        Label fallback = NativeTheme.Body(
            "Local grounded fallback is active. Rook answers only from the current wizard snapshot. "
            + "It cannot apply changes or execute suggestions.",
            NativeTheme.Muted);
        fallback.AutomationId = "rook-local-grounded-fallback";
        _messages.Add(NativeTheme.Card(fallback));

        if (!hasCurrentBinding)
        {
            _messages.Add(NativeTheme.Body(
                "A current revision-bound wizard snapshot is unavailable. Rook is fail-closed.",
                NativeTheme.Danger));
            return;
        }

        Label currentBinding = NativeTheme.Body(
            $"Revision {snapshot!.WorkspaceRevision} · snapshot {ShortDigest(snapshot.SnapshotDigest)}",
            NativeTheme.Muted);
        currentBinding.AutomationId = "rook-current-binding";
        _messages.Add(currentBinding);

        RookConversationThreadState thread = Coordinator.RookConversation;
        if (thread.Messages.Count == 0)
        {
            _messages.Add(NativeTheme.Body(
                "Ask any follow-up. I can report the active stage, exact projected budgets, blockers, "
                + "and legal next stages.",
                NativeTheme.Muted));
            return;
        }

        _messages.Add(NativeTheme.Body($"Workspace thread {thread.ThreadId}", NativeTheme.Muted));
        for (int index = 0; index < thread.Messages.Count; index++)
        {
            RookConversationMessage message = thread.Messages[index];
            bool stale = message.IsStale(snapshot.WorkspaceRevision, snapshot.SnapshotDigest);
            VerticalStackLayout card = new() { Spacing = 7 };
            string author = string.Equals(message.Role, RookConversationRoles.User, StringComparison.Ordinal)
                ? "You"
                : "Rook · local grounded fallback";
            Label binding = NativeTheme.Eyebrow(
                stale
                    ? $"{author} · stale"
                    : $"{author} · revision {message.WorkspaceRevision}");
            binding.AutomationId = $"rook-message-binding-{index}";
            card.Add(binding);
            card.Add(NativeTheme.Body(message.Text, stale ? NativeTheme.Muted : NativeTheme.Text));
            if (stale)
            {
                card.Add(NativeTheme.Body(
                    "This answer belongs to an older runner revision. Ask again for current facts.",
                    NativeTheme.Danger));
            }
            _messages.Add(NativeTheme.Card(card));
        }
    }

    private void QuestionTextChanged(object? sender, TextChangedEventArgs args)
        => _send.IsEnabled = Coordinator.State.CreationWizard is not null
            && !string.IsNullOrWhiteSpace(args.NewTextValue);

    private static string ShortDigest(string digest)
        => string.IsNullOrWhiteSpace(digest)
            ? "unavailable"
            : digest[..Math.Min(12, digest.Length)];
}
