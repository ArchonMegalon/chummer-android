using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class CareerWeaponFirePage : NativePageBase
{
    private sealed record FireAction(CharacterWeaponFireModeState State, Button Button);

    private readonly CareerWeaponFireEditorState _editor;
    private readonly List<FireAction> _actions = [];
    private readonly Button? _defaultFire;

    public CareerWeaponFirePage(
        RunnerSessionCoordinator coordinator,
        CareerWeaponFireEditorState editor) : base(coordinator)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        CharacterWeaponFireState weapon = editor.Weapon;
        if (editor.ContentRevision <= 0
            || !CharacterWeaponFireRules.IsValidIdentity(weapon.Identity)
            || weapon.Modes.Count == 0)
        {
            throw new ArgumentException(
                "Weapon firing requires an exact saved Career Weapon and active clip identity.",
                nameof(editor));
        }

        string token = weapon.Identity.WeaponId.ToString("N");
        Title = "Fire weapon";
        AutomationId = $"career-weapon-fire-page-{token}";
        var body = new VerticalStackLayout
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow("Career ammunition"));
        body.Add(NativeTheme.Title(weapon.DisplayName));
        Label ammo = NativeTheme.Body(
            $"{weapon.AmmoRemaining} rounds in active clip {weapon.Identity.AmmoSlot}",
            NativeTheme.Muted);
        ammo.AutomationId = $"career-weapon-fire-ammo-{token}";
        body.Add(ammo);
        body.Add(NativeTheme.Body(
            "Firing updates both the active saved clip and its linked ammunition stack. Short and long bursts can spend the remaining rounds only after confirmation.",
            NativeTheme.Muted));

        if (weapon.DefaultMode is CharacterWeaponFireMode defaultMode)
        {
            _defaultFire = NativeTheme.PrimaryButton($"FIRE! · {Label(defaultMode)}");
            _defaultFire.AutomationId = $"career-weapon-fire-default-{token}";
            _defaultFire.Clicked += async (_, _) => await RunAsync(() => FireAsync(defaultMode));
            body.Add(_defaultFire);
        }

        body.Add(NativeTheme.FieldLabel("Exact firing modes"));
        foreach (CharacterWeaponFireModeState mode in weapon.Modes)
        {
            Button fire = NativeTheme.SecondaryButton($"{Label(mode.Mode)} · {mode.Rounds} rounds");
            fire.AutomationId = $"career-weapon-fire-{Token(mode.Mode)}-{token}";
            CharacterWeaponFireMode selectedMode = mode.Mode;
            fire.Clicked += async (_, _) => await RunAsync(() => FireAsync(selectedMode));
            _actions.Add(new(mode, fire));
            body.Add(fire);
        }

        Content = new ScrollView { Content = body };
    }

    protected override void Refresh()
    {
        bool current = Coordinator.State.Profile?.Created == true
            && Coordinator.State.WorkspaceId == _editor.WorkspaceId
            && Coordinator.State.ContentRevision == _editor.ContentRevision;
        if (_defaultFire is not null)
        {
            _defaultFire.IsEnabled = current;
        }
        foreach (FireAction action in _actions)
        {
            action.Button.IsEnabled = current;
        }
    }

    private async Task FireAsync(CharacterWeaponFireMode mode)
    {
        CharacterWeaponFireState weapon = _editor.Weapon;
        if (!CharacterWeaponFireRules.TryCreatePlan(
                weapon,
                weapon.Revision,
                mode,
                out CharacterWeaponFirePlan plan))
        {
            await DisplayAlertAsync(
                "Out of ammo",
                "The active clip does not contain enough rounds for that firing mode.",
                "OK");
            return;
        }

        bool confirmedPartial = false;
        if (plan.RequiresPartialConfirmation)
        {
            confirmedPartial = await DisplayAlertAsync(
                "Not enough ammo",
                $"Only {weapon.AmmoRemaining} rounds remain. Fire all remaining rounds as a shortened {Label(mode)}?",
                "Fire remaining",
                "Cancel");
            if (!confirmedPartial)
            {
                return;
            }
        }

        await Coordinator.ApplyCareerWeaponFireAsync(new CareerWeaponFireRequest(
            _editor.WorkspaceId,
            _editor.ContentRevision,
            weapon.Identity,
            weapon.Revision,
            mode,
            confirmedPartial));
        if (Coordinator.State.Error is null)
        {
            await Navigation.PopAsync();
        }
    }

    private static string Label(CharacterWeaponFireMode mode)
        => mode switch
        {
            CharacterWeaponFireMode.SingleShot => "Single Shot",
            CharacterWeaponFireMode.ShortBurst => "Short Burst",
            CharacterWeaponFireMode.LongBurst => "Long Burst",
            CharacterWeaponFireMode.FullBurst => "Full Burst",
            CharacterWeaponFireMode.SuppressiveFire => "Suppressive Fire",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
        };

    private static string Token(CharacterWeaponFireMode mode)
        => mode switch
        {
            CharacterWeaponFireMode.SingleShot => "single-shot",
            CharacterWeaponFireMode.ShortBurst => "short-burst",
            CharacterWeaponFireMode.LongBurst => "long-burst",
            CharacterWeaponFireMode.FullBurst => "full-burst",
            CharacterWeaponFireMode.SuppressiveFire => "suppressive-fire",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
        };
}
