using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class GearSleazeSwapPage : NativePageBase
{
    private sealed record NodeOption(CharacterGearMatrixSwapState State, string Label);
    private sealed record TargetOption(CharacterGearMatrixStat Value, string Label);
    private static readonly IReadOnlyList<TargetOption> Targets =
    [
        new(CharacterGearMatrixStat.Attack, "Attack"),
        new(CharacterGearMatrixStat.DataProcessing, "Data Processing"),
        new(CharacterGearMatrixStat.Firewall, "Firewall")
    ];
    private readonly GearSleazeSwapEditorState _editor;
    private readonly IReadOnlyList<NodeOption> _nodes;
    private readonly Picker _gear;
    private readonly Picker _target;
    private readonly Label _values;
    private readonly Button _save;

    public GearSleazeSwapPage(RunnerSessionCoordinator coordinator, GearSleazeSwapEditorState editor) : base(coordinator)
    {
        _editor = editor;
        if (editor.RootGearId == Guid.Empty || editor.Nodes.Count == 0 || editor.Nodes.Any(node =>
                !CharacterGearMatrixSwapRules.IsValidIdentity(node.Identity)
                || node.Identity.GearPath[0] != editor.RootGearId
                || node.Economics is not { NuyenDelta: 0m, KarmaDelta: 0 }))
            throw new ArgumentException("Gear Sleaze swapping requires exact eligible zero-economic Gear.", nameof(editor));
        string token = editor.RootGearId.ToString("N");
        Title = "Gear Sleaze";
        AutomationId = $"gear-sleaze-swap-page-{token}";
        VerticalStackLayout body = new() { Padding = new Thickness(20, 18, 20, 40), Spacing = 14 };
        body.Add(NativeTheme.Eyebrow("Create + Career Matrix Gear"));
        body.Add(NativeTheme.Title("Swap Sleaze"));
        body.Add(NativeTheme.Body("Swap the saved raw Sleaze value. Bonuses, active/home state, and cost stay unchanged.", NativeTheme.Muted));
        _nodes = editor.Nodes.Select(node => new NodeOption(node,
            $"{node.DisplayPath} · {node.Identity.GearPath[^1].ToString("N")[..8]}")).ToArray();
        _gear = new Picker { Title = "Matrix Gear", ItemsSource = _nodes,
            ItemDisplayBinding = new Binding(nameof(NodeOption.Label)), SelectedIndex = 0,
            AutomationId = $"gear-sleaze-swap-target-{token}", BackgroundColor = NativeTheme.Surface, TextColor = NativeTheme.Text };
        _gear.SelectedIndexChanged += (_, _) => RefreshValues();
        body.Add(_gear);
        _values = NativeTheme.Body(string.Empty, NativeTheme.Muted);
        _values.AutomationId = $"gear-sleaze-swap-values-{token}";
        body.Add(_values);
        _target = new Picker { Title = "Swap Sleaze with", ItemsSource = Targets,
            ItemDisplayBinding = new Binding(nameof(TargetOption.Label)), SelectedIndex = 0,
            AutomationId = $"gear-sleaze-swap-attribute-{token}", BackgroundColor = NativeTheme.Surface, TextColor = NativeTheme.Text };
        body.Add(_target);
        _save = NativeTheme.PrimaryButton("Swap saved Sleaze value");
        _save.AutomationId = $"gear-sleaze-swap-save-{token}";
        _save.Clicked += async (_, _) => await RunAsync(SaveAsync);
        body.Add(_save);
        Content = new ScrollView { Content = body };
        RefreshValues();
    }

    private CharacterGearMatrixSwapState? Selected => _gear.SelectedIndex >= 0 && _gear.SelectedIndex < _nodes.Count
        ? _nodes[_gear.SelectedIndex].State : null;
    protected override void Refresh() => RefreshEnabled();
    private void RefreshValues()
    {
        _values.Text = Selected is { } node
            ? $"Saved raw values · Attack {node.Attack} · Sleaze {node.Sleaze} · Data Processing {node.DataProcessing} · Firewall {node.Firewall}"
            : string.Empty;
        RefreshEnabled();
    }
    private void RefreshEnabled()
    {
        bool current = Coordinator.State.WorkspaceId == _editor.WorkspaceId
            && Coordinator.State.ContentRevision == _editor.ContentRevision;
        _gear.IsEnabled = current; _target.IsEnabled = current && Selected is not null; _save.IsEnabled = _target.IsEnabled;
    }
    private async Task SaveAsync()
    {
        if (Selected is not { } selected || _target.SelectedIndex < 0 || _target.SelectedIndex >= Targets.Count) return;
        CharacterGearMatrixStat target = Targets[_target.SelectedIndex].Value;
        if (string.Equals(selected.Sleaze, CharacterGearMatrixSwapRules.Read(selected, target), StringComparison.Ordinal))
        {
            await DisplayAlertAsync("Values already match", "Choose a different saved raw value.", "OK"); return;
        }
        await Coordinator.ApplyGearSleazeSwapEditAsync(new(_editor.WorkspaceId, _editor.ContentRevision,
            selected.Identity, selected.Revision, CharacterGearMatrixStat.Sleaze, target));
        if (Coordinator.State.Error is null) await Navigation.PopAsync();
    }
}
