using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class GearWirelessPage : NativePageBase
{
    private sealed record NodeOption(CharacterGearWirelessState State, string Label);

    private readonly GearWirelessEditorState _editor;
    private readonly IReadOnlyList<NodeOption> _options;
    private readonly Picker _target;
    private readonly Switch _wireless;
    private readonly Button _save;

    public GearWirelessPage(
        RunnerSessionCoordinator coordinator,
        GearWirelessEditorState editor) : base(coordinator)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        if (editor.RootGearId == Guid.Empty
            || editor.Nodes.Count == 0
            || editor.Nodes.Any(node =>
                !CharacterGearEquipmentRules.IsValidIdentity(node.Identity)
                || node.Identity.GearPath[0] != editor.RootGearId
                || node.Phase != CharacterGearEquipmentPhase.Career
                || !node.CanChangeWireless
                || node.Economics is not { NuyenDelta: 0m, KarmaDelta: 0 }))
        {
            throw new ArgumentException(
                "Gear Wireless editing requires exact zero-economic Career nodes under one stable root Gear.",
                nameof(editor));
        }

        string rootToken = editor.RootGearId.ToString("N");
        Title = "Gear Wireless";
        AutomationId = $"gear-wireless-page-{rootToken}";
        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow("Selected Career Gear tree"));
        body.Add(NativeTheme.Title("Wireless"));
        body.Add(NativeTheme.Body(
            "Choose the exact saved Gear node. Chummer5 exposes this zero-cost switch only in Career mode.",
            NativeTheme.Muted));

        _options = editor.Nodes.Select(node => new NodeOption(
            node,
            $"{node.DisplayPath} · {node.Identity.GearPath[^1].ToString("N")[..8]}"))
            .ToArray();
        _target = new Picker
        {
            Title = "Gear node",
            ItemsSource = (System.Collections.IList)_options,
            ItemDisplayBinding = new Binding(nameof(NodeOption.Label)),
            SelectedIndex = 0,
            AutomationId = $"gear-wireless-target-{rootToken}",
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        _target.SelectedIndexChanged += (_, _) => LoadSelectedState();
        body.Add(NativeTheme.FieldLabel("Selected Gear"));
        body.Add(_target);

        _wireless = new Switch
        {
            AutomationId = $"gear-wireless-toggle-{rootToken}",
            OnColor = NativeTheme.Signal
        };
        SemanticProperties.SetDescription(_wireless, "Wireless enabled");
        Grid row = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        row.Add(NativeTheme.FieldLabel("Wireless enabled"), 0, 0);
        row.Add(_wireless, 1, 0);
        body.Add(NativeTheme.Card(row));

        _save = NativeTheme.PrimaryButton("Save Wireless state");
        _save.AutomationId = $"gear-wireless-save-{rootToken}";
        _save.Clicked += async (_, _) => await RunAsync(SaveAsync);
        body.Add(_save);
        Content = new ScrollView { Content = body };
        LoadSelectedState();
        RefreshEnabledState();
    }

    protected override void Refresh() => RefreshEnabledState();

    private CharacterGearWirelessState? SelectedState
        => _target.SelectedIndex >= 0 && _target.SelectedIndex < _options.Count
            ? _options[_target.SelectedIndex].State
            : null;

    private void LoadSelectedState()
    {
        if (SelectedState is { } selected)
        {
            _wireless.IsToggled = selected.WirelessOn;
        }
        RefreshEnabledState();
    }

    private void RefreshEnabledState()
    {
        bool current = Coordinator.State.WorkspaceId == _editor.WorkspaceId
            && Coordinator.State.ContentRevision == _editor.ContentRevision;
        _target.IsEnabled = current;
        _wireless.IsEnabled = current && SelectedState?.CanChangeWireless == true;
        _save.IsEnabled = current && SelectedState?.CanChangeWireless == true;
    }

    private async Task SaveAsync()
    {
        if (SelectedState is not { } selected)
        {
            await DisplayAlertAsync("Gear required", "Choose one exact saved Gear node before saving.", "OK");
            return;
        }
        if (!selected.CanChangeWireless)
        {
            await DisplayAlertAsync(
                "Wireless state is unavailable",
                "Chummer5 exposes Gear Wireless only in Career mode.",
                "OK");
            return;
        }
        if (_wireless.IsToggled == selected.WirelessOn)
        {
            await Navigation.PopAsync();
            return;
        }

        await Coordinator.ApplyGearWirelessEditAsync(new GearWirelessEditRequest(
            _editor.WorkspaceId,
            _editor.ContentRevision,
            selected.Identity,
            selected.Revision,
            _wireless.IsToggled));
        if (Coordinator.State.Error is null)
        {
            await Navigation.PopAsync();
        }
    }
}
