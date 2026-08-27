using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Chummer.Android.Native;

public interface ISr5CareerCheckpointBackend
{
    string Read();
    void Write(string payload);
    void Remove();
}

public sealed record NativeWorkspaceAuthoritySnapshot(
    string WorkspaceId,
    long ContentRevision,
    long SavedRevision,
    string PayloadSha256,
    string DocumentSha256)
{
    public bool Matches(RunnerStateStub state)
        => state.WorkspaceId is { } id
            && string.Equals(id.Value, WorkspaceId, StringComparison.Ordinal)
            && state.ContentRevision == ContentRevision
            && state.SavedRevision == SavedRevision;
}

public sealed record RunnerProfileStub(bool Created);
public sealed record RunnerRulesStub(string? GameEdition);
public sealed class RunnerStateStub
{
    public RunnerProfileStub? Profile { get; init; }
    public RunnerRulesStub? Rules { get; init; }
    public CharacterWorkspaceId? WorkspaceId { get; init; }
    public long ContentRevision { get; init; }
    public long SavedRevision { get; init; }
    public bool IsDirty { get; init; }
    public string? Error { get; init; }
}

public sealed class RunnerSessionCoordinator
{
    public RunnerStateStub State { get; } = new();
    public Task<NativeWorkspaceAuthoritySnapshot?> CaptureSr5CareerWizardWorkspaceAuthorityAsync(
        CancellationToken cancellationToken = default) => Task.FromResult<NativeWorkspaceAuthoritySnapshot?>(null);
    public Task<CareerCalendarEditorState?> PrepareCareerCalendarEditAsync(
        CancellationToken cancellationToken = default) => Task.FromResult<CareerCalendarEditorState?>(null);
    public Task<bool> ApplyCareerCalendarAddAsync(CareerCalendarAddRequest request,
        CancellationToken cancellationToken = default) => Task.FromResult(false);
    public Task<bool> ApplyCareerCalendarEditAsync(CareerCalendarEditRequest request,
        CancellationToken cancellationToken = default) => Task.FromResult(false);
    public Task<bool> ApplyCareerCalendarDeleteAsync(CareerCalendarDeleteRequest request,
        CancellationToken cancellationToken = default) => Task.FromResult(false);
    public bool SupportsManualAfterRunProposalEntry => false;
}

public static class Sr5CareerWizardCatalog
{
    public static bool IsSr5CareerRunner(bool characterCreated, string? gameEdition)
        => characterCreated && string.Equals(gameEdition, "SR5", StringComparison.OrdinalIgnoreCase);
}

public abstract class NativePageBase : ContentPage
{
    protected NativePageBase(RunnerSessionCoordinator coordinator) => Coordinator = coordinator;
    protected RunnerSessionCoordinator Coordinator { get; }
    protected abstract void Refresh();
    protected async Task RunAsync(Func<Task> action) { await action(); Refresh(); }
}

public static class PhoneShellRoutes
{
    public const string RunnerAbsolute = "//runner";
}

public class HomePage : NativePageBase
{
    public HomePage(RunnerSessionCoordinator coordinator, string route, string title) : base(coordinator) { }
    protected override void Refresh() { }
}

public class MorePage : NativePageBase
{
    public MorePage(RunnerSessionCoordinator coordinator, bool showUnrestrictedActions, string runnerRouteAfterOpen)
        : base(coordinator) { }
    protected override void Refresh() { }
}

public enum Sr5AfterRunCatalogStatus { Missing, Available }
public sealed record Sr5AfterRunSettlementEditorState(
    Sr5AfterRunCatalogStatus Status,
    CharacterWorkspaceId WorkspaceId,
    long WorkspaceRevision);
public interface ISr5CareerCheckpointOwnerAuthority { }
public sealed class PreferencesSr5CareerCheckpointOwnerAuthority : ISr5CareerCheckpointOwnerAuthority { }
public sealed class RunnerSessionSr5AfterRunSettlementPresenter
{
    public RunnerSessionSr5AfterRunSettlementPresenter(RunnerSessionCoordinator coordinator) { }
}
public sealed class Sr5AfterRunSettlementCoordinator
{
    public Sr5AfterRunSettlementCoordinator(
        RunnerSessionSr5AfterRunSettlementPresenter presenter,
        ISr5CareerCheckpointOwnerAuthority owner) { }
    public Task<Sr5AfterRunSettlementEditorState> PrepareAsync()
        => Task.FromResult(new Sr5AfterRunSettlementEditorState(
            Sr5AfterRunCatalogStatus.Missing,
            new CharacterWorkspaceId("stub"),
            1));
}
public sealed class Sr5AfterRunManualProposalPage : ContentPage
{
    public Sr5AfterRunManualProposalPage(
        RunnerSessionCoordinator coordinator,
        CharacterWorkspaceId workspaceId,
        long workspaceRevision) { }
}
public sealed class Sr5AfterRunSettlementWizardPage : ContentPage
{
    public Sr5AfterRunSettlementWizardPage(
        RunnerSessionCoordinator coordinator,
        Sr5AfterRunSettlementEditorState editor) { }
}

public static class NativeTheme
{
    public static Color Paper => Colors.Black;
    public static Color Surface => Colors.DarkGray;
    public static Color Text => Colors.White;
    public static Color Muted => Colors.LightGray;
    public static Color Danger => Colors.Red;
    public static Color Success => Colors.Green;
    public static Label Eyebrow(string value) => new() { Text = value };
    public static Label Title(string value) => new() { Text = value };
    public static Label Body(string value, Color color) => new() { Text = value, TextColor = color };
    public static Label FieldLabel(string value) => new() { Text = value };
    public static Button PrimaryButton(string value) => new() { Text = value };
    public static Button SecondaryButton(string value) => new() { Text = value };
    public static Editor TextArea(string automationId, string semanticDescription, string placeholder)
        => new() { AutomationId = automationId, Placeholder = placeholder };
    public static View Card(View value) => value;
    public static View NavigationRow(
        string title,
        string subtitle,
        Func<Task> action,
        string automationId) => new Button { Text = title, AutomationId = automationId };
}
