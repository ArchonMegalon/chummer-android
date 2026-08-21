using System.Globalization;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class PrototypeTranshumanPage : NativePageBase
{
    private readonly CharacterWorkspaceId _workspaceId;
    private readonly long _contentRevision;
    private readonly CharacterPrototypeTranshumanSemantics _semantics;
    private readonly Switch _prototypeTranshuman;
    private readonly Button _save;

    public PrototypeTranshumanPage(
        RunnerSessionCoordinator coordinator,
        CharacterWorkspaceId workspaceId,
        long contentRevision,
        Guid cyberwareId,
        string cyberwareName,
        CharacterPrototypeTranshumanSemantics semantics) : base(coordinator)
    {
        ArgumentNullException.ThrowIfNull(semantics);
        if (cyberwareId == Guid.Empty
            || semantics.CyberwareId != cyberwareId
            || semantics.EssenceAllowance <= 0m
            || semantics.Hierarchy.Count == 0
            || semantics.Hierarchy[0].CyberwareId != cyberwareId)
        {
            throw new ArgumentException(
                "Prototype Transhuman editing requires exact Create-only Bioware authority bound to one stable hierarchy.",
                nameof(semantics));
        }

        _workspaceId = workspaceId;
        _contentRevision = contentRevision;
        _semantics = semantics;
        string targetToken = cyberwareId.ToString("N");
        Title = "Prototype Transhuman";
        AutomationId = $"prototype-transhuman-page-{targetToken}";

        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow("Selected top-level Bioware"));
        body.Add(NativeTheme.Title(string.IsNullOrEmpty(cyberwareName) ? "Prototype Transhuman" : cyberwareName));
        body.Add(NativeTheme.Body(
            $"Prototype Transhuman allowance: {semantics.EssenceAllowance.ToString(CultureInfo.InvariantCulture)} Essence. " +
            $"Chummer5 applies this choice to all {semantics.Hierarchy.Count.ToString(CultureInfo.InvariantCulture)} saved item(s) in the selected Bioware hierarchy.",
            NativeTheme.Muted));

        _prototypeTranshuman = new Switch
        {
            IsToggled = semantics.PrototypeTranshuman,
            AutomationId = $"prototype-transhuman-toggle-{targetToken}",
            OnColor = NativeTheme.Signal
        };
        Grid row = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        row.Add(NativeTheme.FieldLabel("Prototype Transhuman"), 0, 0);
        row.Add(_prototypeTranshuman, 1, 0);
        body.Add(NativeTheme.Card(row));

        _save = NativeTheme.PrimaryButton("Save Prototype Transhuman");
        _save.AutomationId = $"prototype-transhuman-save-{targetToken}";
        _save.Clicked += async (_, _) => await RunAsync(SaveAsync);
        body.Add(_save);
        Content = new ScrollView { Content = body };
        RefreshEnabledState();
    }

    protected override void Refresh() => RefreshEnabledState();

    private void RefreshEnabledState()
    {
        bool canEdit = Coordinator.State.WorkspaceId == _workspaceId
            && Coordinator.State.ContentRevision == _contentRevision;
        _prototypeTranshuman.IsEnabled = canEdit;
        _save.IsEnabled = canEdit;
    }

    private async Task SaveAsync()
    {
        if (_prototypeTranshuman.IsToggled == _semantics.PrototypeTranshuman)
        {
            await Navigation.PopAsync();
            return;
        }

        await Coordinator.ApplyPrototypeTranshumanEditAsync(new PrototypeTranshumanEditRequest(
            _workspaceId,
            _contentRevision,
            _semantics.CyberwareId,
            _prototypeTranshuman.IsToggled,
            _semantics));
        if (Coordinator.State.Error is null)
        {
            await Navigation.PopAsync();
        }
    }
}
