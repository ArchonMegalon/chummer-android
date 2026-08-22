using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class GearDataProcessingFirewallSwapPage : NativePageBase
{
    private sealed record NodeOption(CharacterGearMatrixSwapState State, string Label);
    private sealed record StatOption(CharacterGearMatrixStat Value, string Label);

    private static readonly StatOption[] ChangedOptions =
    [
        new(CharacterGearMatrixStat.DataProcessing, "Data Processing"),
        new(CharacterGearMatrixStat.Firewall, "Firewall")
    ];

    private static readonly StatOption[] AllTargetOptions =
    [
        new(CharacterGearMatrixStat.Attack, "Attack"),
        new(CharacterGearMatrixStat.Sleaze, "Sleaze"),
        new(CharacterGearMatrixStat.DataProcessing, "Data Processing"),
        new(CharacterGearMatrixStat.Firewall, "Firewall")
    ];

    private readonly GearDataProcessingFirewallSwapEditorState _editor;
    private readonly NodeOption[] _nodes;
    private readonly Picker _gear;
    private readonly Picker _changed;
    private readonly Picker _target;
    private readonly Label _values;
    private readonly Button _save;
    private StatOption[] _targets = [];

    public GearDataProcessingFirewallSwapPage(
        RunnerSessionCoordinator coordinator,
        GearDataProcessingFirewallSwapEditorState editor) : base(coordinator)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        if (editor.RootGearId == Guid.Empty || editor.Nodes.Count == 0 || editor.Nodes.Any(node =>
                !CharacterGearMatrixSwapRules.IsValidIdentity(node.Identity)
                || node.Identity.GearPath[0] != editor.RootGearId
                || node.Economics is not { NuyenDelta: 0m, KarmaDelta: 0 }))
        {
            throw new ArgumentException(
                "Gear Data Processing or Firewall swapping requires exact eligible zero-economic Gear.",
                nameof(editor));
        }

        string token = editor.RootGearId.ToString("N");
        Title = "Gear Data Processing & Firewall";
        AutomationId = $"gear-dp-firewall-swap-page-{token}";
        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow("Create + Career Matrix Gear"));
        body.Add(NativeTheme.Title("Swap Data Processing or Firewall"));
        body.Add(NativeTheme.Body(
            "Swap one saved raw Matrix value. Bonuses, active/home state, and cost stay unchanged; Data Processing only refreshes Matrix initiative consumers.",
            NativeTheme.Muted));

        _nodes = editor.Nodes.Select(node => new NodeOption(
            node, $"{node.DisplayPath} · {node.Identity.GearPath[^1].ToString("N")[..8]}")).ToArray();
        _gear = new Picker
        {
            Title = "Matrix Gear",
            ItemsSource = _nodes,
            ItemDisplayBinding = new Binding(nameof(NodeOption.Label)),
            SelectedIndex = 0,
            AutomationId = $"gear-dp-firewall-swap-gear-{token}",
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        _gear.SelectedIndexChanged += (_, _) => RefreshValues();
        body.Add(NativeTheme.FieldLabel("Selected Gear"));
        body.Add(_gear);

        _values = NativeTheme.Body(string.Empty, NativeTheme.Muted);
        _values.AutomationId = $"gear-dp-firewall-swap-values-{token}";
        body.Add(_values);

        _changed = new Picker
        {
            Title = "Changed Matrix attribute",
            ItemsSource = ChangedOptions,
            ItemDisplayBinding = new Binding(nameof(StatOption.Label)),
            SelectedIndex = 0,
            AutomationId = $"gear-dp-firewall-swap-changed-{token}",
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        body.Add(NativeTheme.FieldLabel("Changed saved attribute"));
        body.Add(_changed);

        _target = new Picker
        {
            Title = "Swap with",
            ItemDisplayBinding = new Binding(nameof(StatOption.Label)),
            AutomationId = $"gear-dp-firewall-swap-target-{token}",
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        _changed.SelectedIndexChanged += (_, _) => RefreshTargetOptions();
        body.Add(NativeTheme.FieldLabel("Other saved attribute"));
        body.Add(_target);

        _save = NativeTheme.PrimaryButton("Swap saved raw values");
        _save.AutomationId = $"gear-dp-firewall-swap-save-{token}";
        _save.Clicked += async (_, _) => await RunAsync(SaveAsync);
        body.Add(_save);
        Content = new ScrollView { Content = body };
        RefreshTargetOptions();
        RefreshValues();
    }

    private CharacterGearMatrixSwapState? Selected
        => _gear.SelectedIndex >= 0 && _gear.SelectedIndex < _nodes.Length
            ? _nodes[_gear.SelectedIndex].State
            : null;

    protected override void Refresh() => RefreshEnabled();

    private void RefreshTargetOptions()
    {
        CharacterGearMatrixStat changed = ChangedOptions[
            _changed.SelectedIndex >= 0 && _changed.SelectedIndex < ChangedOptions.Length
                ? _changed.SelectedIndex
                : 0].Value;
        _targets = AllTargetOptions.Where(option => option.Value != changed).ToArray();
        _target.ItemsSource = _targets;
        _target.SelectedIndex = 0;
        RefreshEnabled();
    }

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
        _gear.IsEnabled = current;
        _changed.IsEnabled = current && Selected is not null;
        _target.IsEnabled = current && Selected is not null && _targets.Length == 3;
        _save.IsEnabled = _target.IsEnabled;
    }

    private async Task SaveAsync()
    {
        if (Selected is not { } selected
            || _changed.SelectedIndex < 0 || _changed.SelectedIndex >= ChangedOptions.Length
            || _target.SelectedIndex < 0 || _target.SelectedIndex >= _targets.Length)
        {
            return;
        }

        CharacterGearMatrixStat changed = ChangedOptions[_changed.SelectedIndex].Value;
        CharacterGearMatrixStat target = _targets[_target.SelectedIndex].Value;
        if (!CharacterGearMatrixSwapRules.TryValidateDataProcessingOrFirewallMutation(
                selected, selected.Revision, changed, target))
        {
            await DisplayAlertAsync(
                "Different values required",
                "Choose two different saved raw Matrix values on eligible Gear.",
                "OK");
            return;
        }

        await Coordinator.ApplyGearDataProcessingFirewallSwapEditAsync(new(
            _editor.WorkspaceId,
            _editor.ContentRevision,
            selected.Identity,
            selected.Revision,
            changed,
            target));
        if (Coordinator.State.Error is null)
        {
            await Navigation.PopAsync();
        }
    }
}
