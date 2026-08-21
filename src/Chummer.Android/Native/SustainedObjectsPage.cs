using System.Globalization;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class SustainedObjectsPage : NativePageBase
{
    private readonly SustainedObjectsEditorState _editor;
    private readonly Switch? _magicianPsyche;
    private readonly Switch? _technomancerPsyche;
    private bool _handlingPsyche;

    public SustainedObjectsPage(
        RunnerSessionCoordinator coordinator,
        SustainedObjectsEditorState editor) : base(coordinator)
    {
        ArgumentNullException.ThrowIfNull(editor);
        _editor = editor;
        Title = "Sustained Effects";
        AutomationId = "sustained-effects-page";

        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow("Active effects"));
        body.Add(NativeTheme.Title("Sustained Effects"));
        body.Add(NativeTheme.Body(
            "Match Chummer5's sustained Spell, Complex Form, and Critter Power controls. "
            + "Duplicate casts remain distinct by their saved occurrence order.",
            NativeTheme.Muted));

        _magicianPsyche = null;
        _technomancerPsyche = null;
        if (editor.PsycheActive.CareerMode
            && (editor.PsycheActive.MagicianControlAvailable
                || editor.PsycheActive.TechnomancerControlAvailable))
        {
            body.Add(NativeTheme.Eyebrow("Psyche"));
            body.Add(NativeTheme.Body(
                "Chummer5 exposes the same saved Psyche state beside sustained Spells and Complex Forms. "
                + "Each visible phone switch updates that one shared runner value.",
                NativeTheme.Muted));
            if (editor.PsycheActive.MagicianControlAvailable)
            {
                _magicianPsyche = AddPsycheSwitch(
                    body,
                    "Psyche active · Magician",
                    "sustained-psyche-active-magician",
                    CharacterPsycheActiveSurface.Magician);
            }
            if (editor.PsycheActive.TechnomancerControlAvailable)
            {
                _technomancerPsyche = AddPsycheSwitch(
                    body,
                    "Psyche active · Technomancer",
                    "sustained-psyche-active-technomancer",
                    CharacterPsycheActiveSurface.Technomancer);
            }
        }

        if (editor.Objects.Count == 0)
        {
            body.Add(NativeTheme.Card(NativeTheme.Body("This runner has no sustained effects.")));
        }
        else
        {
            foreach (CharacterSustainedObjectState state in editor.Objects)
            {
                string token = Token(state.Identity);
                body.Add(NativeTheme.NavigationRow(
                    state.DisplayName,
                    $"{state.Identity.LinkedObjectType} · Force {state.Force} · Net Hits {state.NetHits}",
                    () => Navigation.PushAsync(new SustainedObjectEditPage(
                        Coordinator,
                        editor.WorkspaceId,
                        editor.ContentRevision,
                        state)),
                    automationId: $"sustained-effect-open-{token}"));
            }
        }

        Content = new ScrollView { Content = body };
        RefreshEnabledState();
    }

    protected override void Refresh() => RefreshEnabledState();

    private Switch AddPsycheSwitch(
        VerticalStackLayout body,
        string label,
        string automationId,
        CharacterPsycheActiveSurface surface)
    {
        Switch toggle = new()
        {
            AutomationId = automationId,
            IsToggled = _editor.PsycheActive.Active,
            OnColor = NativeTheme.Signal
        };
        toggle.Toggled += async (_, args) =>
        {
            if (_handlingPsyche || args.Value == _editor.PsycheActive.Active)
            {
                return;
            }
            await RunAsync(() => ApplyPsycheAsync(surface, args.Value));
        };
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
        body.Add(NativeTheme.Card(row));
        return toggle;
    }

    private void RefreshEnabledState()
    {
        bool revisionMatches = Coordinator.State.WorkspaceId == _editor.WorkspaceId
            && Coordinator.State.ContentRevision == _editor.ContentRevision;
        if (!revisionMatches)
        {
            Title = "Sustained Effects · Reload";
        }
        if (_magicianPsyche is not null)
        {
            _magicianPsyche.IsEnabled = revisionMatches
                && CharacterSustainedObjectRules.CanSetPsycheActive(
                    _editor.PsycheActive,
                    CharacterPsycheActiveSurface.Magician,
                    !_editor.PsycheActive.Active);
        }
        if (_technomancerPsyche is not null)
        {
            _technomancerPsyche.IsEnabled = revisionMatches
                && CharacterSustainedObjectRules.CanSetPsycheActive(
                    _editor.PsycheActive,
                    CharacterPsycheActiveSurface.Technomancer,
                    !_editor.PsycheActive.Active);
        }
    }

    private async Task ApplyPsycheAsync(CharacterPsycheActiveSurface surface, bool active)
    {
        _handlingPsyche = true;
        try
        {
            await Coordinator.ApplyPsycheActiveEditAsync(new PsycheActiveEditRequest(
                _editor.WorkspaceId,
                _editor.ContentRevision,
                _editor.PsycheActive,
                surface,
                active));
            if (Coordinator.State.Error is null)
            {
                await Navigation.PopAsync();
                return;
            }

            if (_magicianPsyche is not null)
            {
                _magicianPsyche.IsToggled = _editor.PsycheActive.Active;
            }
            if (_technomancerPsyche is not null)
            {
                _technomancerPsyche.IsToggled = _editor.PsycheActive.Active;
            }
        }
        finally
        {
            _handlingPsyche = false;
        }
    }

    internal static string Token(CharacterSustainedObjectIdentity identity)
        => $"{identity.LinkedObjectType.ToLowerInvariant()}-{identity.LinkedObjectId:N}-{identity.Occurrence.ToString(CultureInfo.InvariantCulture)}";
}

public sealed class SustainedObjectEditPage : NativePageBase
{
    private readonly CharacterWorkspaceId _workspaceId;
    private readonly long _contentRevision;
    private readonly CharacterSustainedObjectState _state;
    private readonly Picker _force;
    private readonly Picker _netHits;
    private readonly Switch? _selfSustained;
    private readonly Button _save;
    private readonly Button _delete;

    public SustainedObjectEditPage(
        RunnerSessionCoordinator coordinator,
        CharacterWorkspaceId workspaceId,
        long contentRevision,
        CharacterSustainedObjectState state) : base(coordinator)
    {
        ArgumentNullException.ThrowIfNull(state);
        _workspaceId = workspaceId;
        _contentRevision = contentRevision;
        _state = state;
        string token = SustainedObjectsPage.Token(state.Identity);
        Title = state.DisplayName;
        AutomationId = $"sustained-effect-editor-{token}";

        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow(state.Identity.LinkedObjectType));
        body.Add(NativeTheme.Title(state.DisplayName));
        body.Add(NativeTheme.Body(
            $"Saved occurrence {(state.Identity.Occurrence + 1).ToString(CultureInfo.InvariantCulture)}. "
            + "Force and Net Hits follow Chummer5's exact 0–100 bounds.",
            NativeTheme.Muted));

        _force = NumberPicker($"sustained-effect-force-{token}", state.Force);
        _netHits = NumberPicker($"sustained-effect-net-hits-{token}", state.NetHits);
        VerticalStackLayout values = new() { Spacing = 10 };
        values.Add(NativeTheme.FieldLabel("Force"));
        values.Add(_force);
        values.Add(NativeTheme.FieldLabel("Net Hits"));
        values.Add(_netHits);

        if (state.SelfSustainedEditable)
        {
            _selfSustained = new Switch
            {
                AutomationId = $"sustained-effect-self-{token}",
                IsToggled = state.SelfSustained,
                OnColor = NativeTheme.Signal
            };
            Grid row = new()
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto)
                }
            };
            row.Add(NativeTheme.FieldLabel("Self sustained"), 0, 0);
            row.Add(_selfSustained, 1, 0);
            values.Add(row);
        }
        else
        {
            values.Add(NativeTheme.Body(
                "Self-Sustained is unavailable for Critter Powers, matching Chummer5.",
                NativeTheme.Muted));
        }
        body.Add(NativeTheme.Card(values));

        _save = NativeTheme.PrimaryButton("Save sustained effect");
        _save.AutomationId = $"sustained-effect-save-{token}";
        _save.Clicked += async (_, _) => await RunAsync(SaveAsync);
        body.Add(_save);

        _delete = NativeTheme.SecondaryButton("Stop sustaining");
        _delete.AutomationId = $"sustained-effect-delete-{token}";
        _delete.TextColor = NativeTheme.Danger;
        _delete.Clicked += async (_, _) =>
        {
            bool confirmed = await DisplayAlertAsync(
                "Stop sustaining?",
                $"Remove the sustained effect for {state.DisplayName}?",
                "Remove",
                "Cancel");
            if (confirmed)
            {
                await RunAsync(DeleteAsync);
            }
        };
        body.Add(_delete);
        Content = new ScrollView { Content = body };
        RefreshEnabledState();
    }

    protected override void Refresh() => RefreshEnabledState();

    private void RefreshEnabledState()
    {
        bool revisionMatches = Coordinator.State.WorkspaceId == _workspaceId
            && Coordinator.State.ContentRevision == _contentRevision;
        _force.IsEnabled = revisionMatches;
        _netHits.IsEnabled = revisionMatches;
        if (_selfSustained is not null)
        {
            _selfSustained.IsEnabled = revisionMatches;
        }
        _save.IsEnabled = revisionMatches;
        _delete.IsEnabled = revisionMatches;
    }

    private async Task SaveAsync()
    {
        int force = Math.Clamp(_force.SelectedIndex, CharacterSustainedObjectRules.MinimumForce, CharacterSustainedObjectRules.MaximumForce);
        int netHits = Math.Clamp(_netHits.SelectedIndex, CharacterSustainedObjectRules.MinimumNetHits, CharacterSustainedObjectRules.MaximumNetHits);
        bool selfSustained = _selfSustained?.IsToggled ?? _state.SelfSustained;
        if (force == _state.Force
            && netHits == _state.NetHits
            && selfSustained == _state.SelfSustained)
        {
            await Navigation.PopAsync();
            return;
        }

        await Coordinator.ApplySustainedObjectEditAsync(new SustainedObjectEditRequest(
            _workspaceId,
            _contentRevision,
            _state,
            CharacterSustainedObjectAction.Update,
            force,
            netHits,
            selfSustained,
            Confirmed: false));
        if (Coordinator.State.Error is null)
        {
            await ReturnToBuildAsync();
        }
    }

    private async Task DeleteAsync()
    {
        await Coordinator.ApplySustainedObjectEditAsync(new SustainedObjectEditRequest(
            _workspaceId,
            _contentRevision,
            _state,
            CharacterSustainedObjectAction.Delete,
            _state.Force,
            _state.NetHits,
            _state.SelfSustained,
            Confirmed: true));
        if (Coordinator.State.Error is null)
        {
            await ReturnToBuildAsync();
        }
    }

    private async Task ReturnToBuildAsync()
    {
        await Navigation.PopAsync();
        await Navigation.PopAsync();
    }

    private static Picker NumberPicker(string automationId, int selected)
    {
        string[] values = Enumerable
            .Range(
                CharacterSustainedObjectRules.MinimumForce,
                CharacterSustainedObjectRules.MaximumForce - CharacterSustainedObjectRules.MinimumForce + 1)
            .Select(static value => value.ToString(CultureInfo.InvariantCulture))
            .ToArray();
        return new Picker
        {
            AutomationId = automationId,
            ItemsSource = values,
            SelectedIndex = Math.Clamp(selected, 0, values.Length - 1),
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text,
            Title = selected.ToString(CultureInfo.InvariantCulture)
        };
    }
}
