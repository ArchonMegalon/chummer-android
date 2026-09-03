using Chummer.Contracts.Characters;
using Chummer.Contracts.Presentation;
using Chummer.Contracts.Workspaces;

namespace Chummer.Contracts.Workspaces
{
    public readonly record struct CharacterWorkspaceId(string Value);
    public sealed record WorkspaceDocument(string Content);
}

namespace Chummer.Contracts.Presentation
{
    public sealed record NavigationTabDefinition(
        string Id,
        string Label,
        string SectionId,
        string Scope,
        bool IsVisible,
        bool IsEnabled,
        string RulesetId);
}

namespace Chummer.Android.Native
{
    public sealed record CompileProfile(bool Created);
    public sealed record CompileRules(string GameEdition);
    public sealed record CompileState(
        CompileProfile? Profile,
        CompileRules? Rules,
        CharacterWorkspaceId? WorkspaceId,
        bool IsDirty,
        long ContentRevision,
        long SavedRevision,
        string? ActiveSectionId,
        string? Error);
    public sealed record CompileSurface(IReadOnlyList<NavigationTabDefinition> NavigationTabs);

    public sealed class RunnerSessionCoordinator
    {
        public CompileState State { get; } = new(null, null, null, false, 0, 0, null, null);
        public CompileSurface Surface { get; } = new([]);
        public bool IsTabEnabled(NavigationTabDefinition tab) => tab.IsEnabled;
        public Task SelectTabAsync(string tabId) => Task.CompletedTask;
        public Sr5CareerCyberwarePurchaseSnapshot LoadCareerCyberwarePurchase()
            => Sr5CareerCyberwarePurchaseSnapshot.Blocked(
                default,
                CharacterCyberwarePurchaseBlockers.SourceAuthorityUnavailable);
        public Sr5CareerCyberwarePurchaseSnapshot UpdateCareerCyberwarePurchaseSelection(
            CharacterCyberwarePurchaseSelection selection) => LoadCareerCyberwarePurchase();
        public Sr5CareerCyberwarePurchaseSnapshot ReviewCareerCyberwarePurchase()
            => LoadCareerCyberwarePurchase();
        public Task<Sr5CareerCyberwarePurchaseSnapshot> ConfirmCareerCyberwarePurchaseAsync()
            => Task.FromResult(LoadCareerCyberwarePurchase());
        public Task<Sr5CareerCyberwarePurchaseSnapshot> UndoCareerCyberwarePurchaseAsync()
            => Task.FromResult(LoadCareerCyberwarePurchase());
        public Sr5CareerCyberwarePurchaseSnapshot ReopenCareerCyberwarePurchase()
            => LoadCareerCyberwarePurchase();
        public Sr5CareerCustomDrugRecipeSnapshot LoadCareerCustomDrugRecipe()
            => Sr5CareerCustomDrugRecipeSnapshot.Blocked(
                default,
                CharacterCustomDrugBlockers.AuthorityUnavailable);
        public Sr5CareerCustomDrugRecipeSnapshot UpdateCareerCustomDrugRecipeSelection(
            CharacterCustomDrugSelection selection) => LoadCareerCustomDrugRecipe();
        public Sr5CareerCustomDrugRecipeSnapshot ReviewCareerCustomDrugRecipe()
            => LoadCareerCustomDrugRecipe();
        public Task<Sr5CareerCustomDrugRecipeSnapshot> ConfirmCareerCustomDrugRecipeAsync()
            => Task.FromResult(LoadCareerCustomDrugRecipe());
        public Task<Sr5CareerCustomDrugRecipeSnapshot> UndoCareerCustomDrugRecipeAsync()
            => Task.FromResult(LoadCareerCustomDrugRecipe());
        public Sr5CareerCustomDrugRecipeSnapshot ReopenCareerCustomDrugRecipe()
            => LoadCareerCustomDrugRecipe();
    }

    public static class Sr5CareerCyberwarePurchaseService
    {
        public static CharacterCyberwarePurchaseSelection EmptySelection { get; } = new(
            new CharacterCyberwareSourceId(Guid.Empty),
            new CharacterCyberwareGradeId(Guid.Empty),
            0,
            0,
            false,
            0m,
            false);
    }

    public static class Sr5CareerCustomDrugRecipeService
    {
        public static CharacterCustomDrugSelection EmptySelection { get; } = new(
            string.Empty,
            new CharacterCustomDrugGradeId(Guid.Empty),
            1m,
            false,
            false,
            0m,
            []);
    }

    public abstract class NativePageBase : ContentPage
    {
        protected NativePageBase(RunnerSessionCoordinator coordinator)
            => Coordinator = coordinator;
        protected RunnerSessionCoordinator Coordinator { get; }
        protected abstract void Refresh();
        protected async Task RunAsync(Func<Task> action) => await action();
        protected new Task<bool> DisplayAlertAsync(
            string title,
            string message,
            string accept,
            string cancel) => Task.FromResult(false);
        protected new Task DisplayAlertAsync(string title, string message, string cancel)
            => Task.CompletedTask;
    }

    public sealed class BuildSectionPage : ContentPage
    {
        public BuildSectionPage(
            RunnerSessionCoordinator coordinator,
            string tabId,
            string title)
        {
            _ = coordinator;
            _ = tabId;
            Title = title;
        }
    }
}
