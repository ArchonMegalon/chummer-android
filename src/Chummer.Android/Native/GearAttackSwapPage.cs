using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class GearAttackSwapPage : NativePageBase
{
    private sealed record NodeOption(CharacterGearAttackSwapState State, string Label);
    private sealed record TargetOption(CharacterGearAttackSwapTarget Value, string Label);

    private static readonly IReadOnlyList<TargetOption> TargetOptions =
    [
        new(CharacterGearAttackSwapTarget.Sleaze, "Sleaze"),
        new(CharacterGearAttackSwapTarget.DataProcessing, "Data Processing"),
        new(CharacterGearAttackSwapTarget.Firewall, "Firewall")
    ];

    private readonly GearAttackSwapEditorState _editor;
    private readonly IReadOnlyList<NodeOption> _options;
    private readonly Picker _gear;
    private readonly Picker _target;
    private readonly Label _rawValues;
    private readonly Button _save;

    public GearAttackSwapPage(RunnerSessionCoordinator coordinator, GearAttackSwapEditorState editor) : base(coordinator)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        if (editor.RootGearId == Guid.Empty
            || editor.Nodes.Count == 0
            || editor.Nodes.Any(node =>
                !CharacterGearAttackSwapRules.IsValidIdentity(node.Identity)
                || node.Identity.GearPath[0] != editor.RootGearId
                || node.Economics is not { NuyenDelta: 0m, KarmaDelta: 0 }))
            throw new ArgumentException(
                "Gear Attack swapping requires exact zero-economic Create/Career Gear under one stable root.", nameof(editor));

        string rootToken = editor.RootGearId.ToString("N");
        Title = "Gear Attack";
        AutomationId = $"gear-attack-swap-page-{rootToken}";
        VerticalStackLayout body = new() { Padding = new Thickness(20, 18, 20, 40), Spacing = 14 };
        body.Add(NativeTheme.Eyebrow("Create + Career Gear"));
        body.Add(NativeTheme.Title("Swap Attack"));
        body.Add(NativeTheme.Body(
            "Swap the selected Gear's saved raw Attack value with one other base Matrix attribute. Bonuses, cost, and state are unchanged.",
            NativeTheme.Muted));

        _options = editor.Nodes.Select(node => new NodeOption(
            node, $"{node.DisplayPath} · {node.Identity.GearPath[^1].ToString("N")[..8]}")).ToArray();
        _gear = new Picker
        {
            Title = "Matrix Gear", ItemsSource = (System.Collections.IList)_options,
            ItemDisplayBinding = new Binding(nameof(NodeOption.Label)), SelectedIndex = 0,
            AutomationId = $"gear-attack-swap-target-{rootToken}",
            BackgroundColor = NativeTheme.Surface, TextColor = NativeTheme.Text
        };
        _gear.SelectedIndexChanged += (_, _) => LoadSelectedState();
        body.Add(NativeTheme.FieldLabel("Selected Gear"));
        body.Add(_gear);

        _rawValues = NativeTheme.Body(string.Empty, NativeTheme.Muted);
        _rawValues.AutomationId = $"gear-attack-swap-values-{rootToken}";
        body.Add(_rawValues);

        _target = new Picker
        {
            Title = "Swap Attack with", ItemsSource = (System.Collections.IList)TargetOptions,
            ItemDisplayBinding = new Binding(nameof(TargetOption.Label)), SelectedIndex = 0,
            AutomationId = $"gear-attack-swap-attribute-{rootToken}",
            BackgroundColor = NativeTheme.Surface, TextColor = NativeTheme.Text
        };
        body.Add(NativeTheme.FieldLabel("Other base attribute"));
        body.Add(_target);

        _save = NativeTheme.PrimaryButton("Swap saved Attack value");
        _save.AutomationId = $"gear-attack-swap-save-{rootToken}";
        _save.Clicked += async (_, _) => await RunAsync(SaveAsync);
        body.Add(_save);
        Content = new ScrollView { Content = body };
        LoadSelectedState();
    }

    protected override void Refresh() => RefreshEnabledState();

    private CharacterGearAttackSwapState? SelectedState
        => _gear.SelectedIndex >= 0 && _gear.SelectedIndex < _options.Count ? _options[_gear.SelectedIndex].State : null;

    private void LoadSelectedState()
    {
        _rawValues.Text = SelectedState is { } node
            ? $"Saved raw values · Attack {node.Attack} · Sleaze {node.Sleaze} · Data Processing {node.DataProcessing} · Firewall {node.Firewall}"
            : string.Empty;
        RefreshEnabledState();
    }

    private void RefreshEnabledState()
    {
        bool current = Coordinator.State.WorkspaceId == _editor.WorkspaceId
            && Coordinator.State.ContentRevision == _editor.ContentRevision;
        _gear.IsEnabled = current;
        _target.IsEnabled = current && SelectedState is not null;
        _save.IsEnabled = current && SelectedState is not null;
    }

    private async Task SaveAsync()
    {
        if (SelectedState is not { } selected
            || _target.SelectedIndex < 0 || _target.SelectedIndex >= TargetOptions.Count)
        {
            await DisplayAlertAsync("Gear and attribute required", "Choose one exact Gear and swap target.", "OK");
            return;
        }
        CharacterGearAttackSwapTarget target = TargetOptions[_target.SelectedIndex].Value;
        if (string.Equals(selected.Attack, CharacterGearAttackSwapRules.ReadTarget(selected, target), StringComparison.Ordinal))
        {
            await DisplayAlertAsync("Values already match", "Choose an attribute whose saved raw value differs from Attack.", "OK");
            return;
        }
        await Coordinator.ApplyGearAttackSwapEditAsync(new GearAttackSwapEditRequest(
            _editor.WorkspaceId, _editor.ContentRevision, selected.Identity, selected.Revision, target));
        if (Coordinator.State.Error is null)
            await Navigation.PopAsync();
    }
}
