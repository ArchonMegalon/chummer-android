using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class ArmorTreeFlagPage : NativePageBase
{
    private sealed record NodeOption(CharacterArmorTreeFlagState State, string Label);

    private readonly ArmorTreeFlagEditorState _editor;
    private readonly IReadOnlyList<NodeOption> _options;
    private readonly Picker _target;
    private readonly Switch _stolen;
    private readonly Switch _discountedCost;
    private readonly Button _save;

    public ArmorTreeFlagPage(
        RunnerSessionCoordinator coordinator,
        ArmorTreeFlagEditorState editor) : base(coordinator)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        if (editor.RootArmorId == Guid.Empty
            || editor.Nodes.Count == 0
            || editor.Nodes.Any(node => node.Identity.ArmorId != editor.RootArmorId
                || !CharacterArmorTreeFlagRules.IsValidIdentity(node.Identity)))
        {
            throw new ArgumentException(
                "Armor-tree flag editing requires exact nodes under one stable root Armor.",
                nameof(editor));
        }

        string rootToken = editor.RootArmorId.ToString("N");
        Title = "Armor tree flags";
        AutomationId = $"armor-tree-flags-page-{rootToken}";
        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow("Selected armor tree"));
        body.Add(NativeTheme.Title("Stolen & Black Market"));
        body.Add(NativeTheme.Body(
            "Choose the exact Armor, ArmorMod, or recursively nested Gear node represented by Chummer5's armor tree.",
            NativeTheme.Muted));

        _options = editor.Nodes.Select(node => new NodeOption(
            node,
            $"{KindLabel(node.Identity.Kind)} · {node.DisplayPath} · {NodeId(node.Identity).ToString("N")[..8]}"))
            .ToArray();
        _target = new Picker
        {
            Title = "Armor-tree node",
            ItemsSource = _options,
            ItemDisplayBinding = new Binding(nameof(NodeOption.Label)),
            SelectedIndex = 0,
            AutomationId = $"armor-tree-flags-target-{rootToken}",
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        _target.SelectedIndexChanged += (_, _) => LoadSelectedState();
        body.Add(NativeTheme.FieldLabel("Selected node"));
        body.Add(_target);

        _stolen = Toggle("Stolen", $"armor-tree-stolen-toggle-{rootToken}");
        body.Add(ToggleCard("Stolen", _stolen));
        _discountedCost = Toggle(
            "Black Market Discount",
            $"armor-tree-discounted-cost-toggle-{rootToken}");
        body.Add(ToggleCard("Black Market Discount (10%)", _discountedCost));

        _save = NativeTheme.PrimaryButton("Save selected node flags");
        _save.AutomationId = $"armor-tree-flags-save-{rootToken}";
        _save.Clicked += async (_, _) => await RunAsync(SaveAsync);
        body.Add(_save);
        Content = new ScrollView { Content = body };
        LoadSelectedState();
        RefreshEnabledState();
    }

    protected override void Refresh() => RefreshEnabledState();

    private void LoadSelectedState()
    {
        if (_target.SelectedIndex < 0 || _target.SelectedIndex >= _options.Count)
        {
            return;
        }
        CharacterArmorTreeFlagState state = _options[_target.SelectedIndex].State;
        _stolen.IsToggled = state.Stolen;
        _discountedCost.IsToggled = state.DiscountedCost;
    }

    private void RefreshEnabledState()
    {
        bool canEdit = Coordinator.State.WorkspaceId == _editor.WorkspaceId
            && Coordinator.State.ContentRevision == _editor.ContentRevision;
        _target.IsEnabled = canEdit;
        _stolen.IsEnabled = canEdit;
        _discountedCost.IsEnabled = canEdit;
        _save.IsEnabled = canEdit;
    }

    private async Task SaveAsync()
    {
        if (_target.SelectedIndex < 0 || _target.SelectedIndex >= _options.Count)
        {
            await DisplayAlertAsync(
                "Armor-tree node required",
                "Choose one exact saved node before saving.",
                "OK");
            return;
        }

        CharacterArmorTreeFlagState selected = _options[_target.SelectedIndex].State;
        if (_stolen.IsToggled == selected.Stolen
            && _discountedCost.IsToggled == selected.DiscountedCost)
        {
            await Navigation.PopAsync();
            return;
        }

        await Coordinator.ApplyArmorTreeFlagEditAsync(new ArmorTreeFlagEditRequest(
            _editor.WorkspaceId,
            _editor.ContentRevision,
            selected.Identity,
            selected.Revision,
            _stolen.IsToggled,
            _discountedCost.IsToggled));
        if (Coordinator.State.Error is null)
        {
            await Navigation.PopAsync();
        }
    }

    private static Switch Toggle(string label, string automationId)
    {
        var toggle = new Switch
        {
            AutomationId = automationId,
            OnColor = NativeTheme.Signal
        };
        SemanticProperties.SetDescription(toggle, label);
        return toggle;
    }

    private static View ToggleCard(string label, Switch toggle)
    {
        Grid row = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        row.Add(NativeTheme.FieldLabel(label), 0, 0);
        row.Add(toggle, 1, 0);
        return NativeTheme.Card(row);
    }

    private static string KindLabel(CharacterArmorTreeNodeKind kind)
        => kind switch
        {
            CharacterArmorTreeNodeKind.Armor => "Armor",
            CharacterArmorTreeNodeKind.ArmorMod => "Armor Mod",
            CharacterArmorTreeNodeKind.Gear => "Gear",
            _ => "Node"
        };

    private static Guid NodeId(CharacterArmorTreeNodeIdentity identity)
        => identity.Kind switch
        {
            CharacterArmorTreeNodeKind.Armor => identity.ArmorId,
            CharacterArmorTreeNodeKind.ArmorMod => identity.ArmorModId!.Value,
            CharacterArmorTreeNodeKind.Gear => identity.GearPath[^1],
            _ => Guid.Empty
        };
}
