using Chummer.Android.Native;

internal static class Program
{
    private static async Task Main()
    {
        (string Name, Func<Task> Run)[] tests =
        [
            (nameof(QueuedOlderUnfocusedCannotOverwriteActionInputAsync), QueuedOlderUnfocusedCannotOverwriteActionInputAsync),
            (nameof(StaleGenerationAndSameIdShapeChangesFailClosedAsync), StaleGenerationAndSameIdShapeChangesFailClosedAsync),
            (nameof(ReadOnlyTransitionFailsClosedAsync), ReadOnlyTransitionFailsClosedAsync),
            (nameof(DoubleTapExecutesExactlyOnceAsync), DoubleTapExecutesExactlyOnceAsync),
            (nameof(CloseWaitsForClaimedActionAsync), CloseWaitsForClaimedActionAsync),
            (nameof(FailureRerendersBeforeQueueAdvancesAsync), FailureRerendersBeforeQueueAdvancesAsync)
        ];

        foreach ((string name, Func<Task> run) in tests)
        {
            await run();
            Console.WriteLine($"PASS {name}");
        }

        Console.WriteLine($"Native dialog interaction tests passed: {tests.Length}");
    }

    private static async Task QueuedOlderUnfocusedCannotOverwriteActionInputAsync()
    {
        NativeDialogInteractionGate gate = new();
        long generation = gate.BeginRender();
        TaskCompletionSource blockerEntered = NewSignal();
        TaskCompletionSource releaseBlocker = NewSignal();
        TaskCompletionSource actionEntered = NewSignal();
        TaskCompletionSource releaseAction = NewSignal();
        List<string> sequence = [];
        string presenterValue = "presenter";
        string currentControlValue = "typed-current";
        int actionCount = 0;
        int failureCount = 0;

        Task blocker = gate.RunFieldUpdateAsync(generation, async () =>
        {
            sequence.Add("blocker");
            blockerEntered.SetResult();
            await releaseBlocker.Task;
        });
        await blockerEntered.Task;

        Task olderUnfocused = gate.RunFieldUpdateAsync(generation, () =>
        {
            presenterValue = "typed-older-capture";
            sequence.Add("older-unfocused");
            return Task.CompletedTask;
        });
        Require(gate.TryClaimAction(), "The first action claim must succeed.");
        Task action = gate.RunClaimedActionAsync(
            async () =>
            {
                sequence.Add("flush");
                Require(
                    presenterValue == "typed-older-capture",
                    "A field update queued before the tap must run before the flush.");
                presenterValue = currentControlValue;
                actionCount++;
                actionEntered.SetResult();
                await releaseAction.Task;
                gate.BeginRender();
            },
            _ =>
            {
                failureCount++;
                return Task.CompletedTask;
            });

        releaseBlocker.SetResult();
        await actionEntered.Task;
        Task staleAfterAction = gate.RunFieldUpdateAsync(generation, () =>
        {
            presenterValue = "stale-overwrite";
            sequence.Add("stale-after-action");
            return Task.CompletedTask;
        });
        releaseAction.SetResult();
        await Task.WhenAll(blocker, olderUnfocused, action, staleAfterAction);

        Require(actionCount == 1, "The action must execute exactly once.");
        Require(failureCount == 0, "The valid action must not use the failure path.");
        Require(
            presenterValue == currentControlValue,
            "The action-bound flush must win with the exact current control value.");
        Require(!sequence.Contains("stale-after-action"), "The old generation must be ignored after the action.");
        Require(
            sequence.IndexOf("older-unfocused") < sequence.IndexOf("flush"),
            "Invocation order must be preserved at the action boundary.");
    }

    private static Task StaleGenerationAndSameIdShapeChangesFailClosedAsync()
    {
        NativeDialogInteractionGate gate = new();
        long firstGeneration = gate.BeginRender();
        NativeDialogFieldBinding binding = NewBinding(firstGeneration);
        Require(Matches(binding, firstGeneration), "The exact rendered shape must match.");

        long secondGeneration = gate.BeginRender();
        Require(
            !Matches(binding, secondGeneration),
            "A same-dialog, same-field binding from an older render must fail closed.");

        NativeDialogFieldBinding current = NewBinding(secondGeneration);
        Require(
            !current.Matches(
                secondGeneration,
                "dialog",
                "field",
                "Alias",
                "Enter alias",
                "text",
                true,
                false,
                "full",
                "default",
                ""),
            "An Entry-to-Editor shape change must fail closed even when the input type is unchanged.");
        Require(
            !current.Matches(
                secondGeneration,
                "dialog",
                "field",
                "Alias",
                "Enter alias",
                "text",
                false,
                false,
                "hidden",
                "default",
                ""),
            "A layout change must fail closed.");
        Require(
            !current.Matches(
                secondGeneration,
                "dialog",
                "field",
                "Alias",
                "Enter alias",
                "text",
                false,
                false,
                "full",
                "detail",
                ""),
            "A visual-kind change must fail closed.");
        return Task.CompletedTask;
    }

    private static Task ReadOnlyTransitionFailsClosedAsync()
    {
        NativeDialogInteractionGate gate = new();
        long generation = gate.BeginRender();
        NativeDialogFieldBinding binding = NewBinding(generation);
        Require(
            !binding.Matches(
                generation,
                "dialog",
                "field",
                "Alias",
                "Enter alias",
                "text",
                false,
                true,
                "full",
                "default",
                ""),
            "An editable field that becomes read-only must fail closed.");
        return Task.CompletedTask;
    }

    private static async Task DoubleTapExecutesExactlyOnceAsync()
    {
        NativeDialogInteractionGate gate = new();
        gate.BeginRender();
        Require(gate.TryClaimAction(), "The first tap must claim the action.");
        Require(!gate.TryClaimAction(), "A second tap must not claim an in-flight action.");
        int actionCount = 0;
        int failureCount = 0;
        await gate.RunClaimedActionAsync(
            () =>
            {
                actionCount++;
                gate.BeginRender();
                return Task.CompletedTask;
            },
            _ =>
            {
                failureCount++;
                return Task.CompletedTask;
            });
        Require(actionCount == 1, "A double tap must execute one action.");
        Require(failureCount == 0, "The double-tap guard must not report a failure.");
    }

    private static async Task CloseWaitsForClaimedActionAsync()
    {
        NativeDialogInteractionGate gate = new();
        gate.BeginRender();
        Require(gate.TryClaimAction(), "The action claim must succeed before the close race.");
        TaskCompletionSource actionEntered = NewSignal();
        TaskCompletionSource releaseAction = NewSignal();
        List<string> sequence = [];

        Task action = gate.RunClaimedActionAsync(
            async () =>
            {
                sequence.Add("action-start");
                actionEntered.SetResult();
                await releaseAction.Task;
                sequence.Add("action-end");
            },
            _ => Task.CompletedTask);
        await actionEntered.Task;

        Task close = gate.RunCloseAsync(() =>
        {
            sequence.Add("close");
            return Task.CompletedTask;
        });
        await Task.Yield();
        Require(!close.IsCompleted, "Close must wait for the claimed action.");
        Require(!gate.TryClaimAction(), "A close request must reject any further action claim.");

        releaseAction.SetResult();
        await Task.WhenAll(action, close);
        Require(
            sequence.SequenceEqual(["action-start", "action-end", "close"]),
            "Close must run after the action without interleaving.");
        Require(gate.IsClosed, "The serialized close must permanently close the interaction gate.");
    }

    private static async Task FailureRerendersBeforeQueueAdvancesAsync()
    {
        NativeDialogInteractionGate gate = new();
        long failedGeneration = gate.BeginRender();
        Require(gate.TryClaimAction(), "The failing action claim must succeed.");
        int executeCount = 0;
        int failureCount = 0;
        int staleMutationCount = 0;
        List<string> sequence = [];

        Task action = gate.RunClaimedActionAsync(
            () =>
            {
                sequence.Add("flush-invalid");
                throw new InvalidOperationException("invalid value");
            },
            _ =>
            {
                failureCount++;
                sequence.Add("rerender");
                gate.BeginRender();
                return Task.CompletedTask;
            });
        Task stale = gate.RunFieldUpdateAsync(failedGeneration, () =>
        {
            staleMutationCount++;
            return Task.CompletedTask;
        });
        await Task.WhenAll(action, stale);

        Require(executeCount == 0, "An invalid flush must not execute the action.");
        Require(failureCount == 1, "The invalid flush must invoke one failure rerender.");
        Require(staleMutationCount == 0, "The rerender must invalidate callbacks queued behind the failure.");
        Require(sequence.SequenceEqual(["flush-invalid", "rerender"]), "Rerender must occur inside the action boundary.");
    }

    private static NativeDialogFieldBinding NewBinding(long generation)
        => new(
            generation,
            "dialog",
            "field",
            "Alias",
            "Enter alias",
            "text",
            false,
            false,
            "full",
            "default",
            "");

    private static bool Matches(NativeDialogFieldBinding binding, long generation)
        => binding.Matches(
            generation,
            "dialog",
            "field",
            "Alias",
            "Enter alias",
            "text",
            false,
            false,
            "full",
            "default",
            "");

    private static TaskCompletionSource NewSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
