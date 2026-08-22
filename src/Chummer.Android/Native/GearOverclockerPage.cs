using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class GearOverclockerPage : NativePageBase
{
    private sealed record NodeOption(CharacterGearOverclockerState State, string Label);
    private sealed record AttributeOption(CharacterGearOverclockerAttribute Value, string Label);

    private static readonly IReadOnlyList<AttributeOption> AttributeOptions =
    [
        new(CharacterGearOverclockerAttribute.None, "None"),
        new(CharacterGearOverclockerAttribute.Attack, "Attack"),
        new(CharacterGearOverclockerAttribute.Sleaze, "Sleaze"),
        new(CharacterGearOverclockerAttribute.DataProcessing, "Data Processing"),
        new(CharacterGearOverclockerAttribute.Firewall, "Firewall")
    ];

    private readonly GearOverclockerEditorState _editor;
    private readonly IReadOnlyList<NodeOption> _options;
    private readonly Picker _target;
    private readonly Picker _attribute;
    private readonly Button _save;

    public GearOverclockerPage(
        RunnerSessionCoordinator coordinator,
        GearOverclockerEditorState editor) : base(coordinator)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        if (editor.RootGearId == Guid.Empty
            || editor.Nodes.Count == 0
            || editor.Nodes.Any(node =>
                !CharacterGearOverclockerRules.IsValidIdentity(node.Identity)
                || node.Identity.GearPath[0] != editor.RootGearId
                || node.Phase != CharacterGearOverclockerPhase.Career
                || node.Economics is not { NuyenDelta: 0m, KarmaDelta: 0 }))
        {
            throw new ArgumentException(
                "Gear Overclocker editing requires exact zero-economic Career Cyberdecks under one stable root Gear.",
                nameof(editor));
        }

        string rootToken = editor.RootGearId.ToString("N");
        Title = "Gear Overclocker";
        AutomationId = $"gear-overclocker-page-{rootToken}";
        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow("Career Cyberdeck Gear"));
        body.Add(NativeTheme.Title("Overclocker"));
        body.Add(NativeTheme.Body(
            "Choose the exact saved Cyberdeck and Matrix attribute. Overclocker adds one to that attribute without a Nuyen or Karma transaction.",
            NativeTheme.Muted));

        _options = editor.Nodes.Select(node => new NodeOption(
            node,
            $"{node.DisplayPath} · {node.Identity.GearPath[^1].ToString("N")[..8]}"))
            .ToArray();
        _target = new Picker
        {
            Title = "Cyberdeck Gear",
            ItemsSource = _options,
            ItemDisplayBinding = new Binding(nameof(NodeOption.Label)),
            SelectedIndex = 0,
            AutomationId = $"gear-overclocker-target-{rootToken}",
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        _target.SelectedIndexChanged += (_, _) => LoadSelectedState();
        body.Add(NativeTheme.FieldLabel("Selected Cyberdeck"));
        body.Add(_target);

        _attribute = new Picker
        {
            Title = "Boosted Matrix attribute",
            ItemsSource = AttributeOptions,
            ItemDisplayBinding = new Binding(nameof(AttributeOption.Label)),
            AutomationId = $"gear-overclocker-attribute-{rootToken}",
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        body.Add(NativeTheme.FieldLabel("Overclocked attribute"));
        body.Add(_attribute);

        _save = NativeTheme.PrimaryButton("Save Overclocker attribute");
        _save.AutomationId = $"gear-overclocker-save-{rootToken}";
        _save.Clicked += async (_, _) => await RunAsync(SaveAsync);
        body.Add(_save);
        Content = new ScrollView { Content = body };
        LoadSelectedState();
        RefreshEnabledState();
    }

    protected override void Refresh() => RefreshEnabledState();

    private CharacterGearOverclockerState? SelectedState
        => _target.SelectedIndex >= 0 && _target.SelectedIndex < _options.Count
            ? _options[_target.SelectedIndex].State
            : null;

    private void LoadSelectedState()
    {
        if (SelectedState is { } selected)
        {
            _attribute.SelectedIndex = AttributeOptions
                .Select((option, index) => (option, index))
                .Single(pair => pair.option.Value == selected.Attribute)
                .index;
        }
        RefreshEnabledState();
    }

    private void RefreshEnabledState()
    {
        bool current = Coordinator.State.WorkspaceId == _editor.WorkspaceId
            && Coordinator.State.ContentRevision == _editor.ContentRevision;
        _target.IsEnabled = current;
        _attribute.IsEnabled = current && SelectedState is not null;
        _save.IsEnabled = current && SelectedState is not null;
    }

    private async Task SaveAsync()
    {
        if (SelectedState is not { } selected
            || _attribute.SelectedIndex < 0
            || _attribute.SelectedIndex >= AttributeOptions.Count)
        {
            await DisplayAlertAsync(
                "Cyberdeck and attribute required",
                "Choose one exact saved Cyberdeck and Matrix attribute before saving.",
                "OK");
            return;
        }

        CharacterGearOverclockerAttribute attribute =
            AttributeOptions[_attribute.SelectedIndex].Value;
        if (attribute == selected.Attribute)
        {
            await Navigation.PopAsync();
            return;
        }

        await Coordinator.ApplyGearOverclockerEditAsync(new GearOverclockerEditRequest(
            _editor.WorkspaceId,
            _editor.ContentRevision,
            selected.Identity,
            selected.Revision,
            attribute));
        if (Coordinator.State.Error is null)
        {
            await Navigation.PopAsync();
        }
    }
}
