using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class CyberwareMatrixSwapPage : NativePageBase
{
    private sealed record StatOption(CharacterCyberwareMatrixStat Value, string Label);

    private static readonly StatOption[] ChangedOptions =
    [
        new(CharacterCyberwareMatrixStat.Attack, "Attack"),
        new(CharacterCyberwareMatrixStat.Sleaze, "Sleaze"),
        new(CharacterCyberwareMatrixStat.DataProcessing, "Data Processing"),
        new(CharacterCyberwareMatrixStat.Firewall, "Firewall")
    ];

    private static readonly StatOption[] AllTargets =
    [
        new(CharacterCyberwareMatrixStat.Attack, "Attack"),
        new(CharacterCyberwareMatrixStat.Sleaze, "Sleaze"),
        new(CharacterCyberwareMatrixStat.DataProcessing, "Data Processing"),
        new(CharacterCyberwareMatrixStat.Firewall, "Firewall")
    ];

    private readonly CyberwareMatrixSwapEditorState _editor;
    private readonly Picker _changed;
    private readonly Picker _target;
    private readonly Button _save;
    private StatOption[] _targets = [];

    public CyberwareMatrixSwapPage(
        RunnerSessionCoordinator coordinator,
        CyberwareMatrixSwapEditorState editor) : base(coordinator)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        CharacterCyberwareMatrixSwapState cyberware = editor.Cyberware;
        if (!CharacterCyberwareMatrixSwapRules.IsValidIdentity(cyberware.Identity)
            || cyberware.Economics is not { NuyenDelta: 0m, KarmaDelta: 0 }
            || !cyberware.Provenance.CanSwapAttributes
            || string.IsNullOrEmpty(cyberware.Provenance.AttributeArray))
        {
            throw new ArgumentException(
                "Cyberware Matrix swapping requires an exact eligible root Cyberware node.", nameof(editor));
        }

        string token = cyberware.Identity.CyberwareId.ToString("N");
        Title = "Cyberware Matrix values";
        AutomationId = $"cyberware-matrix-swap-page-{token}";
        var body = new VerticalStackLayout { Padding = new Thickness(20, 18, 20, 40), Spacing = 14 };
        body.Add(NativeTheme.Eyebrow("Create + Career Cyberware Matrix"));
        body.Add(NativeTheme.Title(cyberware.DisplayName));
        body.Add(NativeTheme.Body(
            "Swap one saved raw Attack, Sleaze, Data Processing, or Firewall value on this Cyberware root. Bonuses, descendants, state, and costs remain unchanged.",
            NativeTheme.Muted));
        Label values = NativeTheme.Body(
            $"Saved raw values · Attack {cyberware.Attack} · Sleaze {cyberware.Sleaze} · Data Processing {cyberware.DataProcessing} · Firewall {cyberware.Firewall}",
            NativeTheme.Muted);
        values.AutomationId = $"cyberware-matrix-swap-values-{token}";
        body.Add(values);

        _changed = new Picker
        {
            Title = "Changed Matrix attribute",
            ItemsSource = ChangedOptions,
            ItemDisplayBinding = new Binding(nameof(StatOption.Label)),
            SelectedIndex = 0,
            AutomationId = $"cyberware-matrix-swap-changed-{token}",
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        _changed.SelectedIndexChanged += (_, _) => RefreshTargets();
        body.Add(NativeTheme.FieldLabel("Changed saved attribute"));
        body.Add(_changed);

        _target = new Picker
        {
            Title = "Swap with",
            ItemDisplayBinding = new Binding(nameof(StatOption.Label)),
            AutomationId = $"cyberware-matrix-swap-target-{token}",
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        body.Add(NativeTheme.FieldLabel("Other saved attribute"));
        body.Add(_target);

        _save = NativeTheme.PrimaryButton("Swap saved raw values");
        _save.AutomationId = $"cyberware-matrix-swap-save-{token}";
        _save.Clicked += async (_, _) => await RunAsync(SaveAsync);
        body.Add(_save);
        Content = new ScrollView { Content = body };
        RefreshTargets();
    }

    protected override void Refresh() => RefreshEnabled();

    private void RefreshTargets()
    {
        CharacterCyberwareMatrixStat changed = ChangedOptions[
            _changed.SelectedIndex >= 0 && _changed.SelectedIndex < ChangedOptions.Length
                ? _changed.SelectedIndex
                : 0].Value;
        _targets = AllTargets.Where(option => option.Value != changed).ToArray();
        _target.ItemsSource = _targets;
        _target.SelectedIndex = 0;
        RefreshEnabled();
    }

    private void RefreshEnabled()
    {
        bool current = Coordinator.State.WorkspaceId == _editor.WorkspaceId
            && Coordinator.State.ContentRevision == _editor.ContentRevision;
        _changed.IsEnabled = current;
        _target.IsEnabled = current && _targets.Length == AllTargets.Length - 1;
        _save.IsEnabled = _target.IsEnabled;
    }

    private async Task SaveAsync()
    {
        if (_changed.SelectedIndex < 0 || _changed.SelectedIndex >= ChangedOptions.Length
            || _target.SelectedIndex < 0 || _target.SelectedIndex >= _targets.Length)
        {
            return;
        }

        CharacterCyberwareMatrixStat changed = ChangedOptions[_changed.SelectedIndex].Value;
        CharacterCyberwareMatrixStat target = _targets[_target.SelectedIndex].Value;
        if (!CharacterCyberwareMatrixSwapRules.TryValidateMutation(
                _editor.Cyberware, _editor.Cyberware.Revision, changed, target))
        {
            await DisplayAlertAsync(
                "Different values required",
                "Choose two different saved raw Matrix values on the eligible Cyberware root.",
                "OK");
            return;
        }

        await Coordinator.ApplyCyberwareMatrixSwapEditAsync(new(
            _editor.WorkspaceId,
            _editor.ContentRevision,
            _editor.Cyberware.Identity,
            _editor.Cyberware.Revision,
            changed,
            target));
        if (Coordinator.State.Error is null)
            await Navigation.PopAsync();
    }
}
