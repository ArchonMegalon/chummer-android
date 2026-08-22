using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class WeaponMatrixSwapPage : NativePageBase
{
    private sealed record StatOption(CharacterWeaponMatrixStat Value, string Label);

    private static readonly StatOption[] ChangedOptions =
    [
        new(CharacterWeaponMatrixStat.Attack, "Attack"),
        new(CharacterWeaponMatrixStat.Sleaze, "Sleaze"),
        new(CharacterWeaponMatrixStat.DataProcessing, "Data Processing"),
        new(CharacterWeaponMatrixStat.Firewall, "Firewall")
    ];

    private static readonly StatOption[] AllTargets =
    [
        new(CharacterWeaponMatrixStat.Attack, "Attack"),
        new(CharacterWeaponMatrixStat.Sleaze, "Sleaze"),
        new(CharacterWeaponMatrixStat.DataProcessing, "Data Processing"),
        new(CharacterWeaponMatrixStat.Firewall, "Firewall")
    ];

    private readonly WeaponMatrixSwapEditorState _editor;
    private readonly Picker _changed;
    private readonly Picker _target;
    private readonly Button _save;
    private StatOption[] _targets = [];

    public WeaponMatrixSwapPage(
        RunnerSessionCoordinator coordinator,
        WeaponMatrixSwapEditorState editor) : base(coordinator)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        CharacterWeaponMatrixSwapState weapon = editor.Weapon;
        if (!CharacterWeaponMatrixSwapRules.IsValidIdentity(weapon.Identity)
            || weapon.Phase != CharacterWeaponMatrixSwapPhase.Career
            || weapon.Economics is not { NuyenDelta: 0m, KarmaDelta: 0 }
            || !weapon.Provenance.CanSwapAttributes
            || !string.Equals(
                weapon.Provenance.LegacySurface,
                CharacterWeaponMatrixSwapRules.LegacySurface,
                StringComparison.Ordinal)
            || string.IsNullOrEmpty(weapon.Provenance.AttributeArray))
        {
            throw new ArgumentException(
                "Weapon Matrix swapping requires an exact eligible Career root Weapon.",
                nameof(editor));
        }

        string token = weapon.Identity.WeaponId.ToString("N");
        Title = "Weapon Matrix values";
        AutomationId = $"weapon-matrix-swap-page-{token}";
        var body = new VerticalStackLayout
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow("Career Weapon Matrix"));
        body.Add(NativeTheme.Title(weapon.DisplayName));
        body.Add(NativeTheme.Body(
            "Swap one saved raw Attack, Sleaze, Data Processing, or Firewall value on this direct Weapon. Descendants, bonuses, state, and costs remain unchanged.",
            NativeTheme.Muted));
        Label values = NativeTheme.Body(
            $"Saved raw values · Attack {weapon.Attack} · Sleaze {weapon.Sleaze} · Data Processing {weapon.DataProcessing} · Firewall {weapon.Firewall}",
            NativeTheme.Muted);
        values.AutomationId = $"weapon-matrix-swap-values-{token}";
        body.Add(values);

        _changed = new Picker
        {
            Title = "Changed Matrix attribute",
            ItemsSource = ChangedOptions,
            ItemDisplayBinding = new Binding(nameof(StatOption.Label)),
            SelectedIndex = 0,
            AutomationId = $"weapon-matrix-swap-changed-{token}",
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
            AutomationId = $"weapon-matrix-swap-target-{token}",
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        body.Add(NativeTheme.FieldLabel("Other saved attribute"));
        body.Add(_target);

        _save = NativeTheme.PrimaryButton("Swap saved raw values");
        _save.AutomationId = $"weapon-matrix-swap-save-{token}";
        _save.Clicked += async (_, _) => await RunAsync(SaveAsync);
        body.Add(_save);
        Content = new ScrollView { Content = body };
        RefreshTargets();
    }

    protected override void Refresh() => RefreshEnabled();

    private void RefreshTargets()
    {
        CharacterWeaponMatrixStat changed = ChangedOptions[
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
        bool current = Coordinator.State.Profile?.Created == true
            && Coordinator.State.WorkspaceId == _editor.WorkspaceId
            && Coordinator.State.ContentRevision == _editor.ContentRevision;
        _changed.IsEnabled = current;
        _target.IsEnabled = current && _targets.Length == AllTargets.Length - 1;
        _save.IsEnabled = _target.IsEnabled;
    }

    private async Task SaveAsync()
    {
        if (_changed.SelectedIndex < 0
            || _changed.SelectedIndex >= ChangedOptions.Length
            || _target.SelectedIndex < 0
            || _target.SelectedIndex >= _targets.Length)
        {
            return;
        }

        CharacterWeaponMatrixStat changed = ChangedOptions[_changed.SelectedIndex].Value;
        CharacterWeaponMatrixStat target = _targets[_target.SelectedIndex].Value;
        if (!CharacterWeaponMatrixSwapRules.TryValidateMutation(
                _editor.Weapon,
                _editor.Weapon.Revision,
                changed,
                target))
        {
            await DisplayAlertAsync(
                "Different values required",
                "Choose two different saved raw Matrix values on the eligible Weapon root.",
                "OK");
            return;
        }

        await Coordinator.ApplyWeaponMatrixSwapEditAsync(new WeaponMatrixSwapEditRequest(
            _editor.WorkspaceId,
            _editor.ContentRevision,
            _editor.Weapon.Identity,
            _editor.Weapon.Revision,
            changed,
            target));
        if (Coordinator.State.Error is null)
        {
            await Navigation.PopAsync();
        }
    }
}
