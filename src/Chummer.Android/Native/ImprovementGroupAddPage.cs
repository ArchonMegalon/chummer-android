using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class ImprovementGroupAddPage : NativePageBase
{
    private readonly ImprovementGroupAddEditorState _editor;
    private readonly Entry _name;
    private readonly Label _summary;
    private readonly Button _add;

    public ImprovementGroupAddPage(
        RunnerSessionCoordinator coordinator,
        ImprovementGroupAddEditorState editor) : base(coordinator)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        if (editor.Collection.Revision.Length != CharacterImprovementGroupAddRules.RevisionHexLength
            || editor.Collection.Groups.Any(group => group is null)
            || editor.Collection.Economics is not { KarmaDelta: 0, NuyenDelta: 0m })
        {
            throw new ArgumentException(
                "Add Improvement Group requires exact Career collection state.",
                nameof(editor));
        }

        Title = "Add Improvement Group";
        AutomationId = "improvement-group-add-page";
        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow("Career improvements"));
        body.Add(NativeTheme.Title("Add a group"));
        body.Add(NativeTheme.Body(
            "Append the exact Chummer5 group name. Spaces and duplicate names are preserved.",
            NativeTheme.Muted));

        _summary = NativeTheme.Body(
            $"{editor.Collection.Groups.Count} saved groups · 0 Karma · 0 Nuyen",
            NativeTheme.Muted);
        _summary.AutomationId = "improvement-group-add-summary";
        body.Add(NativeTheme.Card(_summary));

        _name = new Entry
        {
            AutomationId = "improvement-group-add-name",
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text,
            Placeholder = "Group name"
        };
        SemanticProperties.SetDescription(_name, "Improvement group name");
        _name.TextChanged += (_, _) => RefreshEnabledState();
        body.Add(NativeTheme.FieldLabel("Group name"));
        body.Add(_name);

        _add = NativeTheme.PrimaryButton("Add Group");
        _add.AutomationId = "improvement-group-add-save";
        _add.Clicked += async (_, _) => await RunAsync(SaveAsync);
        body.Add(_add);
        Content = new ScrollView { Content = body };
        RefreshEnabledState();
    }

    protected override void Refresh() => RefreshEnabledState();

    private void RefreshEnabledState()
    {
        bool current = Coordinator.State.WorkspaceId == _editor.WorkspaceId
            && Coordinator.State.ContentRevision == _editor.ContentRevision;
        _name.IsEnabled = current;
        _add.IsEnabled = current
            && CharacterImprovementGroupAddRules.IsValidNewName(_name.Text);
    }

    private async Task SaveAsync()
    {
        string name = _name.Text ?? string.Empty;
        if (!CharacterImprovementGroupAddRules.TryCreateIdentity(
                _editor.Collection,
                name,
                out CharacterImprovementGroupInsertionIdentity identity))
        {
            await DisplayAlertAsync(
                "Group name required",
                "Enter a non-empty Improvement group name.",
                "OK");
            return;
        }

        await Coordinator.ApplyImprovementGroupAddAsync(new ImprovementGroupAddRequest(
            _editor.WorkspaceId,
            _editor.ContentRevision,
            identity,
            _editor.Collection.Revision));
        if (Coordinator.State.Error is null)
        {
            await Navigation.PopAsync();
        }
    }
}
