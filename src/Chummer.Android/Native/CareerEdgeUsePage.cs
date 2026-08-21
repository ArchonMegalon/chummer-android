using System.Globalization;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class CareerEdgeUsePage : NativePageBase
{
    private readonly CareerEdgeUseEditorState _editor;
    private readonly Label _summary;
    private readonly Button _spend;
    private readonly Button _regain;

    public CareerEdgeUsePage(
        RunnerSessionCoordinator coordinator,
        CareerEdgeUseEditorState editor) : base(coordinator)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        Title = "Edge use";
        AutomationId = "career-edge-use-page";

        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow("Career runner"));
        body.Add(NativeTheme.Title("Current Edge"));
        body.Add(NativeTheme.Body(
            "Match Chummer5's Spend Edge and Regain Edge controls. Each action changes saved EdgeUsed by exactly one.",
            NativeTheme.Muted));
        _summary = NativeTheme.Body(FormatSummary(editor.Edge));
        _summary.AutomationId = "career-edge-use-summary";
        body.Add(NativeTheme.Card(_summary));

        _spend = NativeTheme.PrimaryButton("Spend 1 Edge");
        _spend.AutomationId = "career-edge-use-spend";
        _spend.Clicked += async (_, _) => await RunAsync(() => ApplyAsync(CharacterCareerEdgeUseAction.Spend));
        body.Add(_spend);

        _regain = NativeTheme.SecondaryButton("Regain 1 Edge");
        _regain.AutomationId = "career-edge-use-regain";
        _regain.Clicked += async (_, _) => await RunAsync(() => ApplyAsync(CharacterCareerEdgeUseAction.Regain));
        body.Add(_regain);
        Content = new ScrollView { Content = body };
        RefreshEnabledState();
    }

    protected override void Refresh() => RefreshEnabledState();

    private void RefreshEnabledState()
    {
        bool current = Coordinator.State.WorkspaceId == _editor.WorkspaceId
            && Coordinator.State.ContentRevision == _editor.ContentRevision;
        _spend.IsEnabled = current && _editor.Edge.CanSpend;
        _regain.IsEnabled = current && _editor.Edge.CanRegain;
        _summary.Text = FormatSummary(_editor.Edge);
    }

    private async Task ApplyAsync(CharacterCareerEdgeUseAction action)
    {
        await Coordinator.ApplyCareerEdgeUseEditAsync(new CareerEdgeUseEditRequest(
            _editor.WorkspaceId,
            _editor.ContentRevision,
            _editor.Edge,
            action));
        if (Coordinator.State.Error is null)
        {
            await Navigation.PopAsync();
        }
    }

    private static string FormatSummary(CharacterCareerEdgeUseState state)
        => $"{state.AvailableEdge.ToString(CultureInfo.InvariantCulture)} available · "
            + $"{state.EdgeUsed.ToString(CultureInfo.InvariantCulture)} used · "
            + $"{state.TotalEdge.ToString(CultureInfo.InvariantCulture)} total";
}
