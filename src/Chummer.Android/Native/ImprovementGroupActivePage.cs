using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class ImprovementGroupActivePage : NativePageBase
{
    private sealed record GroupOption(CharacterImprovementGroupActiveState State, string Label);

    private readonly ImprovementGroupActiveEditorState _editor;
    private readonly IReadOnlyList<GroupOption> _options;
    private readonly Picker _target;
    private readonly Label _summary;
    private readonly Button _enableAll;
    private readonly Button _disableAll;

    public ImprovementGroupActivePage(
        RunnerSessionCoordinator coordinator,
        ImprovementGroupActiveEditorState editor) : base(coordinator)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        if (editor.Groups.Any(group =>
                !CharacterImprovementGroupActiveRules.IsValidIdentity(group.Identity)
                || group.Members.Any(member =>
                    !CharacterImprovementActiveRules.IsValidIdentity(member.Identity))))
        {
            throw new ArgumentException(
                "Improvement groups require exact stable saved identities.",
                nameof(editor));
        }

        Title = "Improvement groups";
        AutomationId = "improvement-group-active-page";
        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow("Career improvements"));
        body.Add(NativeTheme.Title("Enable or disable a group"));
        body.Add(NativeTheme.Body(
            "Choose the ungrouped custom Improvement root or one exact saved custom group.",
            NativeTheme.Muted));

        _options = editor.Groups.Select(group => new GroupOption(
                group,
                $"{group.DisplayName} · {group.Members.Count} custom improvements"))
            .ToArray();
        _target = new Picker
        {
            Title = "Improvement group",
            ItemsSource = _options,
            ItemDisplayBinding = new Binding(nameof(GroupOption.Label)),
            SelectedIndex = _options.Count == 0 ? -1 : 0,
            AutomationId = "improvement-group-active-target",
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        _target.SelectedIndexChanged += (_, _) => RefreshEnabledState();
        body.Add(NativeTheme.FieldLabel("Selected group"));
        body.Add(_target);

        _summary = NativeTheme.Body(string.Empty, NativeTheme.Muted);
        _summary.AutomationId = "improvement-group-active-summary";
        body.Add(NativeTheme.Card(_summary));

        _enableAll = NativeTheme.PrimaryButton("Enable All");
        _enableAll.AutomationId = "improvement-group-enable-all";
        _enableAll.Clicked += async (_, _) => await RunAsync(() => SaveAsync(enabled: true));
        body.Add(_enableAll);

        _disableAll = NativeTheme.SecondaryButton("Disable All");
        _disableAll.AutomationId = "improvement-group-disable-all";
        _disableAll.Clicked += async (_, _) => await RunAsync(() => SaveAsync(enabled: false));
        body.Add(_disableAll);
        Content = new ScrollView { Content = body };
        RefreshEnabledState();
    }

    protected override void Refresh() => RefreshEnabledState();

    private void RefreshEnabledState()
    {
        bool current = Coordinator.State.WorkspaceId == _editor.WorkspaceId
            && Coordinator.State.ContentRevision == _editor.ContentRevision;
        CharacterImprovementGroupActiveState? selected = SelectedState();
        _target.IsEnabled = current && _options.Count != 0;
        _enableAll.IsEnabled = current && selected?.DisabledCount > 0;
        _disableAll.IsEnabled = current && selected?.EnabledCount > 0;
        _summary.Text = selected is null
            ? "No exact saved custom Improvement group is available."
            : $"{selected.EnabledCount} enabled · {selected.DisabledCount} disabled";
    }

    private CharacterImprovementGroupActiveState? SelectedState()
        => _target.SelectedIndex >= 0 && _target.SelectedIndex < _options.Count
            ? _options[_target.SelectedIndex].State
            : null;

    private async Task SaveAsync(bool enabled)
    {
        CharacterImprovementGroupActiveState? selected = SelectedState();
        if (selected is null)
        {
            await DisplayAlertAsync(
                "Improvement group required",
                "Choose one exact saved custom Improvement group.",
                "OK");
            return;
        }

        await Coordinator.ApplyImprovementGroupActiveEditAsync(
            new ImprovementGroupActiveEditRequest(
                _editor.WorkspaceId,
                _editor.ContentRevision,
                selected.Identity,
                selected.Revision,
                enabled));
        if (Coordinator.State.Error is null)
        {
            await Navigation.PopAsync();
        }
    }
}
