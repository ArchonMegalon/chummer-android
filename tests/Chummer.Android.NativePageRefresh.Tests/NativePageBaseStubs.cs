using Chummer.Presentation.Overview;
using Microsoft.Maui.Controls;

namespace Chummer.Presentation.Overview
{
    public sealed record DesktopDialogState(string Id = "dialog.test");
}

namespace Chummer.Android.Native
{
    public sealed record FakeWorkspaceId(string Value);

    public sealed class FakeCoordinatorState
    {
        public DesktopDialogState? ActiveDialog { get; set; }

        public FakeWorkspaceId? WorkspaceId { get; set; }

        public long ContentRevision { get; set; }

        public long SavedRevision { get; set; }
    }

    public sealed record FakeAccountSnapshot(bool IsLoading);

    public sealed class FakeAccountLinkService
    {
        public event EventHandler? Changed;

        public FakeAccountSnapshot Snapshot { get; private set; } = new(true);

        public void Publish(bool isLoading, int burst = 1)
        {
            Snapshot = new(isLoading);
            for (int index = 0; index < burst; index++)
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public sealed class RunnerSessionCoordinator
    {
        private readonly Func<Task> _initialize;

        public RunnerSessionCoordinator(
            FakeAccountLinkService account,
            Func<Task>? initialize = null)
        {
            Account = account;
            _initialize = initialize ?? (() => Task.CompletedTask);
            account.Changed += (_, args) => Changed?.Invoke(this, args);
        }

        public event EventHandler? Changed;

        public FakeAccountLinkService Account { get; }

        public FakeCoordinatorState State { get; } = new();

        public Task InitializeAsync() => _initialize();
    }

    public static class NativeTheme
    {
        public static Color Paper => Colors.White;
    }

    public sealed class NativeDialogPage : ContentPage
    {
        public NativeDialogPage(
            RunnerSessionCoordinator coordinator,
            DesktopDialogState dialog)
        {
        }

        public event EventHandler? Closed
        {
            add { }
            remove { }
        }
    }

    public interface IPlayReviewService
    {
        void SignalMeaningfulSuccess();

        Task TryRequestAtSafeMomentAsync(
            PlayReviewSafetyContext safety,
            CancellationToken cancellationToken = default);
    }

    public readonly record struct PlayReviewSafetyContext(bool IsSafe);

    public static class PlayReviewSafety
    {
        public static PlayReviewSafetyContext Capture(RunnerSessionCoordinator coordinator)
            => new(false);
    }

    public static class PlayReviewInteractionGuard
    {
        public static void EnterAction()
        {
        }

        public static void ExitAction()
        {
        }
    }
}
