using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Chummer.Android.Native;

public sealed record RunnerProfileStub(bool Created);
public sealed record RunnerRulesStub(string? GameEdition);

public sealed class RunnerStateStub
{
    public RunnerProfileStub? Profile { get; init; } = new(true);
    public RunnerRulesStub? Rules { get; init; } = new("SR5");
    public CharacterWorkspaceId? WorkspaceId { get; init; } = new("workspace-table-stub");
    public long ContentRevision { get; init; } = 1;
    public long SavedRevision { get; init; } = 1;
    public bool IsDirty { get; init; }
    public string? Error { get; init; }
    public ConditionMonitorEditorState? ActiveConditionMonitor { get; init; }
}

public sealed class RunnerSessionCoordinator
{
    public RunnerStateStub State { get; } = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task<CareerEdgeUseEditorState?> PrepareCareerEdgeUseEditAsync(
        CancellationToken cancellationToken = default)
        => Task.FromResult<CareerEdgeUseEditorState?>(null);

    public Task<CareerWeaponFireCatalogEditorState?> PrepareCareerWeaponFireCatalogAsync(
        CancellationToken cancellationToken = default)
        => Task.FromResult<CareerWeaponFireCatalogEditorState?>(null);

    public Task ApplyCareerEdgeUseEditAsync(
        CareerEdgeUseEditRequest request,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task ApplyCareerWeaponFireAsync(
        CareerWeaponFireRequest request,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task ApplyConditionMonitorEditAsync(
        ConditionMonitorEditRequest request,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

public interface ISr5CareerCheckpointBackend
{
    string Read();
    void Write(string payload);
    void Remove();
}

public static class Sr5CareerWizardCatalog
{
    public static bool IsSr5CareerRunner(bool characterCreated, string? gameEdition)
        => characterCreated
           && string.Equals(gameEdition, "SR5", StringComparison.OrdinalIgnoreCase);
}

public static class Sr5CareerWizardRoutes
{
    public const string BeforeRun = "sr5-career/before-run";
    public const string BeforeRunReview = "sr5-career/before-run/review";
    public const string Playtime = "sr5-career/playtime";
    public const string PlaytimeReview = "sr5-career/playtime/review";
}

public abstract class NativePageBase : ContentPage
{
    protected NativePageBase(RunnerSessionCoordinator coordinator)
        => Coordinator = coordinator;

    protected RunnerSessionCoordinator Coordinator { get; }
    protected abstract void Refresh();

    protected async Task RunAsync(Func<Task> action)
    {
        await action();
        Refresh();
    }
}

public static class NativeTheme
{
    public static Color Muted => Colors.Gray;
    public static Color Danger => Colors.Red;
    public static Label Eyebrow(string value) => new() { Text = value };
    public static Label Title(string value) => new() { Text = value };
    public static Label Body(string value) => new() { Text = value };
    public static Label Body(string value, Color color) => new() { Text = value, TextColor = color };
    public static Label FieldLabel(string value) => new() { Text = value };
    public static Button PrimaryButton(string value) => new() { Text = value };
    public static Button SecondaryButton(string value) => new() { Text = value };
    public static View Card(View value) => value;
    public static View NavigationRow(
        string title,
        string subtitle,
        Func<Task> action,
        bool enabled = true,
        string? automationId = null)
        => new Button
        {
            Text = title,
            IsEnabled = enabled,
            AutomationId = automationId
        };
}
