using Chummer.Contracts.Presentation;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class BuildPage : NativePageBase
{
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 16
    };
    private readonly ToolbarItem _save;

    public BuildPage(RunnerSessionCoordinator coordinator) : base(coordinator)
    {
        Title = "Build";
        _save = new ToolbarItem
        {
            Text = "Save",
            Command = new Command(async () => await RunAsync(() => Coordinator.SaveAsync()))
        };
        ToolbarItems.Add(_save);
        ToolbarItems.Add(new ToolbarItem
        {
            Text = "Actions",
            Order = ToolbarItemOrder.Primary,
            Priority = 1,
            Command = new Command(async () => await Navigation.PushAsync(new NativeCommandPage(Coordinator)))
        });
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _save.IsEnabled = Coordinator.State.Profile is not null;
        if (Coordinator.State.Profile is null)
        {
            _body.Add(NativeTheme.Eyebrow("Build"));
            _body.Add(NativeTheme.Title("Open a runner first"));
            _body.Add(NativeTheme.Body("Your file stays on this device unless you choose to link it.", NativeTheme.Muted));
            Button open = NativeTheme.PrimaryButton("Open file");
            open.Clicked += async (_, _) => await RunAsync(() => Coordinator.OpenLocalAsync());
            _body.Add(open);
            return;
        }

        AddWorkspacePicker();
        AddSummary();
        AddDossier();
        AddBuildAreas();
        AddTools();

        if (!string.IsNullOrWhiteSpace(Coordinator.State.Error ?? Coordinator.Surface.Error))
        {
            _body.Add(NativeTheme.Body(Coordinator.State.Error ?? Coordinator.Surface.Error!, NativeTheme.Danger));
        }
        else if (!string.IsNullOrWhiteSpace(Coordinator.Notice))
        {
            _body.Add(NativeTheme.Body(Coordinator.Notice!, NativeTheme.Muted));
        }
    }

    private void AddDossier()
    {
        _body.Add(NativeTheme.Eyebrow("Runner"));
        _body.Add(NativeTheme.NavigationRow(
            "Origin dossier",
            "Identity, appearance and story",
            () => Navigation.PushAsync(new OriginDossierPage(Coordinator)),
            automationId: "build-origin-dossier"));
    }

    private void AddWorkspacePicker()
    {
        IReadOnlyList<OpenWorkspaceState> workspaces = Coordinator.State.OpenWorkspaces;
        if (workspaces.Count <= 1)
        {
            return;
        }

        string[] labels = workspaces.Select(static workspace =>
            !string.IsNullOrWhiteSpace(workspace.Alias) ? workspace.Alias : workspace.Name).ToArray();
        Picker picker = new()
        {
            Title = "Runner",
            ItemsSource = labels,
            SelectedIndex = Math.Max(0, workspaces.ToList().FindIndex(workspace =>
                workspace.Id == Coordinator.State.WorkspaceId)),
            BackgroundColor = NativeTheme.Surface
        };
        picker.SelectedIndexChanged += async (_, _) =>
        {
            if (picker.SelectedIndex >= 0)
            {
                await RunAsync(() => Coordinator.SwitchWorkspaceAsync(workspaces[picker.SelectedIndex]));
            }
        };
        _body.Add(picker);
    }

    private void AddSummary()
    {
        string name = Coordinator.State.Profile?.Alias
            ?? Coordinator.State.Profile?.Name
            ?? "Runner";
        VerticalStackLayout summary = new() { Spacing = 10 };
        summary.Add(NativeTheme.Eyebrow(Coordinator.State.IsDirty ? "Unsaved changes" : "Runner"));
        summary.Add(NativeTheme.Title(name, 24));
        summary.Add(NativeTheme.Metric("Metatype", Coordinator.State.Profile?.Metatype ?? string.Empty));
        summary.Add(NativeTheme.Metric("Rules", Coordinator.State.Rules?.GameEdition ?? string.Empty));
        summary.Add(NativeTheme.Metric("Karma", Coordinator.State.Progress?.Karma.ToString() ?? string.Empty));
        summary.Add(NativeTheme.Metric("Nuyen", Coordinator.State.Progress?.Nuyen.ToString() ?? string.Empty));
        _body.Add(NativeTheme.Card(summary));
    }

    private void AddBuildAreas()
    {
        _body.Add(NativeTheme.Eyebrow("Build areas"));
        foreach (NavigationTabDefinition tab in Coordinator.Surface.NavigationTabs)
        {
            string title = RunnerSessionCoordinator.HumanizeId(tab.Id);
            bool enabled = Coordinator.IsTabEnabled(tab);
            bool active = string.Equals(tab.Id, Coordinator.Surface.ActiveTabId, StringComparison.Ordinal);
            string detail = active && Coordinator.State.ActiveSectionRows.Count > 0
                ? $"{Coordinator.State.ActiveSectionRows.Count} details"
                : "Open section";
            _body.Add(NativeTheme.NavigationRow(
                title,
                detail,
                () => RunAsync(async () =>
                {
                    await Coordinator.SelectTabAsync(tab.Id);
                    await Navigation.PushAsync(new BuildSectionPage(Coordinator, tab.Id, title));
                }),
                enabled,
                $"build-section-{tab.Id}"));
        }
    }

    private void AddTools()
    {
        _body.Add(NativeTheme.Eyebrow("Tools"));
        _body.Add(NativeTheme.NavigationRow(
            "All actions",
            "Search every runner command",
            () => Navigation.PushAsync(new NativeCommandPage(Coordinator))));
    }
}
