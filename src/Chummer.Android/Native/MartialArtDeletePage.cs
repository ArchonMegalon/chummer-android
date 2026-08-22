using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class MartialArtDeletePage : NativePageBase
{
    private sealed record TargetOption(CharacterMartialArtDeleteState State, string Label);

    private readonly MartialArtDeleteEditorState _editor;
    private readonly IReadOnlyList<TargetOption> _options;
    private readonly Picker _target;
    private readonly Label _warning;
    private readonly Button _delete;

    public MartialArtDeletePage(
        RunnerSessionCoordinator coordinator,
        MartialArtDeleteEditorState editor) : base(coordinator)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        if (editor.Targets.Any(target =>
                !CharacterMartialArtDeleteRules.IsValidIdentity(target.Identity)
                || target.Revision.Length != CharacterMartialArtDeleteRules.RevisionHexLength
                || target.Economics is not { KarmaDelta: 0, NuyenDelta: 0m }))
        {
            throw new ArgumentException(
                "Martial Art deletion requires exact stable identity, revision, and zero-refund state.",
                nameof(editor));
        }

        Title = "Delete Martial Art";
        AutomationId = "martial-art-delete-page";
        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow(editor.Targets.FirstOrDefault()?.Created == true ? "Career" : "Creation"));
        body.Add(NativeTheme.Title("Delete Martial Art or Technique"));
        body.Add(NativeTheme.Body(
            "Choose one removable Martial Art or parent-scoped Technique. Quality-backed Arts are protected.",
            NativeTheme.Muted));

        _options = editor.Targets
            .Where(static target => target.CanDelete)
            .Select(target => new TargetOption(target, Label(target)))
            .ToArray();
        _target = new Picker
        {
            Title = "Martial Art or Technique",
            ItemsSource = _options,
            ItemDisplayBinding = new Binding(nameof(TargetOption.Label)),
            SelectedIndex = _options.Count == 0 ? -1 : 0,
            AutomationId = "martial-art-delete-target",
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        _target.SelectedIndexChanged += (_, _) => RefreshEnabledState();
        body.Add(NativeTheme.FieldLabel("Selected Martial Art or Technique"));
        body.Add(_target);

        _warning = NativeTheme.Body(
            "Deletion requires confirmation and never refunds Karma or Nuyen.",
            NativeTheme.Warning);
        _warning.AutomationId = "martial-art-delete-warning";
        body.Add(_warning);

        _delete = NativeTheme.PrimaryButton("Delete");
        _delete.BackgroundColor = NativeTheme.Danger;
        _delete.AutomationId = "martial-art-delete-confirm";
        _delete.Clicked += async (_, _) => await RunAsync(DeleteAsync);
        body.Add(_delete);
        Content = new ScrollView { Content = body };
        RefreshEnabledState();
    }

    protected override void Refresh() => RefreshEnabledState();

    private CharacterMartialArtDeleteState? SelectedState()
        => _target.SelectedIndex >= 0 && _target.SelectedIndex < _options.Count
            ? _options[_target.SelectedIndex].State
            : null;

    private void RefreshEnabledState()
    {
        bool current = Coordinator.State.WorkspaceId == _editor.WorkspaceId
            && Coordinator.State.ContentRevision == _editor.ContentRevision
            && SelectedState() is not null;
        _target.IsEnabled = current;
        _delete.IsEnabled = current;
    }

    private async Task DeleteAsync()
    {
        CharacterMartialArtDeleteState? selected = SelectedState();
        if (selected is null)
        {
            await DisplayAlertAsync("Target required", "Choose a Martial Art or Technique.", "OK");
            return;
        }
        string message = selected.Identity.IsTechnique
            ? $"Delete {selected.TargetName} from {selected.MartialArtName}? Its exact Technique improvements will also be removed."
            : $"Delete {selected.TargetName} and its {selected.CascadeTechniqueCount} Techniques? Exact linked improvements will also be removed.";
        bool confirmed = await DisplayAlertAsync(
            "Delete Martial Art?",
            message,
            "Delete",
            "Cancel");
        if (!confirmed)
        {
            return;
        }

        await Coordinator.ApplyMartialArtDeleteAsync(new MartialArtDeleteRequest(
            _editor.WorkspaceId,
            _editor.ContentRevision,
            selected.Identity,
            selected.Revision,
            Confirmed: true));
        if (Coordinator.State.Error is null)
        {
            await Navigation.PopAsync();
        }
    }

    private static string Label(CharacterMartialArtDeleteState state)
    {
        Guid id = state.Identity.TechniqueId ?? state.Identity.MartialArtId;
        string token = id.ToString("N")[..8];
        return state.Identity.IsTechnique
            ? $"Technique · {state.MartialArtName} > {state.TargetName} · {token}"
            : $"Art · {state.TargetName} · {token}";
    }
}
