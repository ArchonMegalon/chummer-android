using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Chummer.Presentation.Overview
{
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

    internal static class DesktopLocalizationCatalog
    {
        public const string DefaultLanguage = "en-us";

        public static string NormalizeOrDefault(string? languageCode)
            => string.Equals(languageCode, DefaultLanguage, StringComparison.OrdinalIgnoreCase)
                ? DefaultLanguage
                : DefaultLanguage;

        public static string GetCurrentLanguage() => DefaultLanguage;

        public static string GetChummer5ExpenseRefundLabel(string languageCode)
            => "Refund";
    }
}

namespace Chummer.Android.Native
{
    public static class Sr5CareerWizardPage
    {
        public static string LaneToken(Sr5CareerWizardLane lane)
            => lane.ToString().ToLowerInvariant();
    }

    internal sealed record RunnerSessionProfileStub(bool Created);
    internal sealed record RunnerSessionRulesStub(string? GameEdition);

    internal sealed class RunnerSessionStateStub
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
        internal RunnerSessionStateStub State { get; } = new();

        public Task InitializeAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<Chummer.Presentation.Overview.CareerActiveSkillAdvanceEditorState?>
            PrepareCareerActiveSkillAdvanceAsync(CancellationToken cancellationToken)
            => Task.FromResult<Chummer.Presentation.Overview.CareerActiveSkillAdvanceEditorState?>(null);

        public Task<Chummer.Presentation.Overview.CareerKarmaExpenseEditorState?>
            PrepareCareerKarmaExpenseEditAsync(CancellationToken cancellationToken)
            => Task.FromResult<Chummer.Presentation.Overview.CareerKarmaExpenseEditorState?>(null);

        public Task<bool> ApplyCareerActiveSkillAdvanceAsync(
            Chummer.Presentation.Overview.CareerActiveSkillAdvanceRequest request,
            CancellationToken cancellationToken)
            => Task.FromResult(false);
    }

    public abstract class NativePageBase : ContentPage
    {
        protected NativePageBase(RunnerSessionCoordinator coordinator)
        {
            Coordinator = coordinator;
        }

        protected RunnerSessionCoordinator Coordinator { get; }

        protected virtual void Refresh()
        {
        }

        protected Task RunAsync(Func<Task> action) => action();

        protected new Task DisplayAlertAsync(string title, string message, string cancel)
            => Task.CompletedTask;

        protected new Task<bool> DisplayAlertAsync(
            string title,
            string message,
            string accept,
            string cancel)
            => Task.FromResult(false);
    }

    public static class NativeTheme
    {
        public static Color Surface { get; } = Colors.Black;
        public static Color Text { get; } = Colors.White;
        public static Color Danger { get; } = Colors.Red;
        public static Color Muted { get; } = Colors.Gray;

        public static Label Eyebrow(string text) => new() { Text = text };
        public static Label Title(string text) => new() { Text = text };
        public static Label FieldLabel(string text) => new() { Text = text };
        public static Label Body(string text, Color color) => new() { Text = text, TextColor = color };
        public static Label Metric(string label, string value) => new() { Text = $"{label}: {value}" };
        public static ContentView Card(View content) => new() { Content = content };
        public static Button PrimaryButton(string text) => new() { Text = text };
        public static Button SecondaryButton(string text) => new() { Text = text };
    }
}
