using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class VehicleWeaponFiringModeListPage : NativePageBase
{
    private readonly VehicleWeaponFiringModeEditorState _editor;
    private readonly List<Border> _rows = [];

    public VehicleWeaponFiringModeListPage(
        RunnerSessionCoordinator coordinator,
        VehicleWeaponFiringModeEditorState editor) : base(coordinator)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        if (editor.VehicleId == Guid.Empty || editor.ContentRevision <= 0 || editor.Weapons.Count == 0
            || editor.Weapons.Any(weapon =>
                !CharacterVehicleWeaponFiringModeRules.IsValidIdentity(weapon.Identity)
                || weapon.Identity.VehicleId != editor.VehicleId
                || weapon.Economics is not { NuyenDelta: 0m, KarmaDelta: 0 }))
        {
            throw new ArgumentException(
                "Vehicle Weapon firing-mode navigation requires exact eligible direct weapons.", nameof(editor));
        }

        string vehicleToken = editor.VehicleId.ToString("N");
        Title = "Vehicle weapons";
        AutomationId = $"vehicle-weapon-firing-mode-list-{vehicleToken}";
        var body = new VerticalStackLayout { Padding = new Thickness(20, 18, 20, 40), Spacing = 12 };
        body.Add(NativeTheme.Eyebrow("Create + Career Vehicle Weapon firing mode"));
        body.Add(NativeTheme.Title(editor.VehicleDisplayName));
        body.Add(NativeTheme.Body(
            "Choose one direct Vehicle Weapon. Mount, underbarrel, accessory, and other descendant paths remain unavailable.",
            NativeTheme.Muted));
        foreach (CharacterVehicleWeaponFiringModeState weapon in editor.Weapons)
        {
            string token = Token(weapon.Identity);
            Border row = NativeTheme.NavigationRow(
                weapon.DisplayName,
                $"Saved mode · {LabelFor(weapon.FiringMode)}",
                () => Navigation.PushAsync(new VehicleWeaponFiringModePage(Coordinator, editor, weapon)),
                automationId: $"vehicle-weapon-firing-mode-open-{token}");
            _rows.Add(row);
            body.Add(row);
        }
        Content = new ScrollView { Content = body };
        Refresh();
    }

    protected override void Refresh()
    {
        bool current = Coordinator.State.WorkspaceId == _editor.WorkspaceId
            && Coordinator.State.ContentRevision == _editor.ContentRevision;
        foreach (Border row in _rows)
        {
            row.IsEnabled = current;
            row.Opacity = current ? 1 : 0.55;
        }
    }

    internal static string Token(CharacterVehicleWeaponFiringModeIdentity identity)
        => $"{identity.VehicleId:N}-{identity.WeaponId:N}";

    internal static string LabelFor(CharacterVehicleWeaponFiringMode mode) => mode switch
    {
        CharacterVehicleWeaponFiringMode.DogBrain => "Dog Brain",
        CharacterVehicleWeaponFiringMode.GunneryCommandDevice => "Gunnery Command Device",
        CharacterVehicleWeaponFiringMode.RemoteOperated => "Remote Operated",
        CharacterVehicleWeaponFiringMode.ManualOperation => "Manual Operation",
        CharacterVehicleWeaponFiringMode.Skill => "Skill",
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };
}

public sealed class VehicleWeaponFiringModePage : NativePageBase
{
    private sealed record ModeOption(CharacterVehicleWeaponFiringMode Value, string Label);

    private static readonly ModeOption[] Options =
    [
        new(CharacterVehicleWeaponFiringMode.DogBrain, "Dog Brain"),
        new(CharacterVehicleWeaponFiringMode.GunneryCommandDevice, "Gunnery Command Device"),
        new(CharacterVehicleWeaponFiringMode.RemoteOperated, "Remote Operated"),
        new(CharacterVehicleWeaponFiringMode.ManualOperation, "Manual Operation"),
        new(CharacterVehicleWeaponFiringMode.Skill, "Skill")
    ];

    private readonly VehicleWeaponFiringModeEditorState _editor;
    private readonly CharacterVehicleWeaponFiringModeState _weapon;
    private readonly Picker _mode;
    private readonly Button _save;

    public VehicleWeaponFiringModePage(
        RunnerSessionCoordinator coordinator,
        VehicleWeaponFiringModeEditorState editor,
        CharacterVehicleWeaponFiringModeState weapon) : base(coordinator)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _weapon = weapon ?? throw new ArgumentNullException(nameof(weapon));
        if (!CharacterVehicleWeaponFiringModeRules.IsValidIdentity(weapon.Identity)
            || weapon.Identity.VehicleId != editor.VehicleId
            || weapon.Economics is not { NuyenDelta: 0m, KarmaDelta: 0 }
            || !editor.Weapons.Any(candidate => candidate.Identity == weapon.Identity
                && string.Equals(candidate.Revision, weapon.Revision, StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                "Vehicle Weapon firing-mode editing requires one exact eligible direct weapon.", nameof(weapon));
        }

        string token = VehicleWeaponFiringModeListPage.Token(weapon.Identity);
        Title = "Firing mode";
        AutomationId = $"vehicle-weapon-firing-mode-page-{token}";
        var body = new VerticalStackLayout { Padding = new Thickness(20, 18, 20, 40), Spacing = 14 };
        body.Add(NativeTheme.Eyebrow("Create + Career Vehicle Weapon"));
        body.Add(NativeTheme.Title(weapon.DisplayName));
        body.Add(NativeTheme.Body(
            "Change only the saved firing-mode enum. Ammo, range type, descendants, parent vehicle, Nuyen, and Karma stay unchanged.",
            NativeTheme.Muted));
        body.Add(NativeTheme.FieldLabel("Firing mode"));
        _mode = new Picker
        {
            Title = "Firing mode",
            ItemsSource = Options,
            ItemDisplayBinding = new Binding(nameof(ModeOption.Label)),
            SelectedIndex = Array.FindIndex(Options, option => option.Value == weapon.FiringMode),
            AutomationId = $"vehicle-weapon-firing-mode-picker-{token}",
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        _mode.SelectedIndexChanged += (_, _) => RefreshEnabled();
        body.Add(_mode);
        _save = NativeTheme.PrimaryButton("Save firing mode");
        _save.AutomationId = $"vehicle-weapon-firing-mode-save-{token}";
        _save.Clicked += async (_, _) => await RunAsync(SaveAsync);
        body.Add(_save);
        Content = new ScrollView { Content = body };
        RefreshEnabled();
    }

    protected override void Refresh() => RefreshEnabled();

    private void RefreshEnabled()
    {
        bool current = Coordinator.State.WorkspaceId == _editor.WorkspaceId
            && Coordinator.State.ContentRevision == _editor.ContentRevision;
        _mode.IsEnabled = current;
        _save.IsEnabled = current
            && _mode.SelectedIndex >= 0
            && _mode.SelectedIndex < Options.Length
            && Options[_mode.SelectedIndex].Value != _weapon.FiringMode;
    }

    private async Task SaveAsync()
    {
        if (_mode.SelectedIndex < 0 || _mode.SelectedIndex >= Options.Length)
            return;
        CharacterVehicleWeaponFiringMode requested = Options[_mode.SelectedIndex].Value;
        if (!CharacterVehicleWeaponFiringModeRules.TryValidateMutation(
                _weapon, _weapon.Revision, requested))
        {
            await DisplayAlertAsync(
                "Different valid mode required",
                "Choose one of the five legacy firing modes and change the current value.",
                "OK");
            return;
        }

        await Coordinator.ApplyVehicleWeaponFiringModeEditAsync(new(
            _editor.WorkspaceId,
            _editor.ContentRevision,
            _weapon.Identity,
            _weapon.Revision,
            requested));
        if (Coordinator.State.Error is null)
            await Navigation.PopAsync();
    }
}
