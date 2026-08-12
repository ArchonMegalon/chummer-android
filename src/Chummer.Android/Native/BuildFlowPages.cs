using Chummer.Contracts.Presentation;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class BuildSectionPage : NativePageBase
{
    private readonly string _tabId;
    private readonly string _title;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };

    public BuildSectionPage(RunnerSessionCoordinator coordinator, string tabId, string title) : base(coordinator)
    {
        _tabId = tabId;
        _title = title;
        Title = title;
        ToolbarItems.Add(new ToolbarItem
        {
            Text = "Save",
            Command = new Command(async () => await RunAsync(() => Coordinator.SaveAsync()))
        });
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
        string runner = Coordinator.State.Profile?.Alias
            ?? Coordinator.State.Profile?.Name
            ?? "Runner";
        _body.Add(NativeTheme.Eyebrow(runner));
        _body.Add(NativeTheme.Title(_title));

        AddSectionActions();
        AddValueGroups();

        if (!string.IsNullOrWhiteSpace(Coordinator.State.Error ?? Coordinator.Surface.Error))
        {
            _body.Add(NativeTheme.Body(Coordinator.State.Error ?? Coordinator.Surface.Error!, NativeTheme.Danger));
        }
    }

    private void AddSectionActions()
    {
        if (Coordinator.Surface.WorkspaceActions.Count == 0)
        {
            return;
        }

        _body.Add(NativeTheme.Eyebrow("Actions"));
        foreach (WorkspaceSurfaceActionDefinition action in Coordinator.Surface.WorkspaceActions)
        {
            _body.Add(NativeTheme.NavigationRow(
                action.Label,
                null,
                () => RunAsync(() => Coordinator.ExecuteWorkspaceActionAsync(action))));
        }
    }

    private void AddValueGroups()
    {
        IReadOnlyList<SectionRowState> rows = Coordinator.State.ActiveSectionRows;
        _body.Add(NativeTheme.Eyebrow("Details"));
        if (rows.Count == 0)
        {
            _body.Add(NativeTheme.Body("Nothing to show here yet.", NativeTheme.Muted));
            return;
        }

        foreach (IGrouping<string, SectionRowState> group in rows
            .GroupBy(static row => BuildNavigation.GroupKey(row.Path), StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.CurrentCultureIgnoreCase))
        {
            string groupKey = group.Key;
            int count = group.Count();
            _body.Add(NativeTheme.NavigationRow(
                RunnerSessionCoordinator.HumanizeId(groupKey),
                count == 1 ? "1 detail" : $"{count} details",
                () => Navigation.PushAsync(new BuildValueGroupPage(Coordinator, _tabId, _title, groupKey))));
        }
    }
}

public sealed class BuildValueGroupPage : NativePageBase
{
    private readonly string _tabId;
    private readonly string _groupKey;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };

    public BuildValueGroupPage(
        RunnerSessionCoordinator coordinator,
        string tabId,
        string sectionTitle,
        string groupKey) : base(coordinator)
    {
        _tabId = tabId;
        _groupKey = groupKey;
        Title = RunnerSessionCoordinator.HumanizeId(groupKey);
        ToolbarItems.Add(new ToolbarItem
        {
            Text = "Save",
            Command = new Command(async () => await RunAsync(() => Coordinator.SaveAsync()))
        });
        Content = new ScrollView { Content = _body };
        SemanticProperties.SetDescription(this, $"{sectionTitle}, {Title}");
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow(RunnerSessionCoordinator.HumanizeId(_tabId)));
        _body.Add(NativeTheme.Title(RunnerSessionCoordinator.HumanizeId(_groupKey)));

        SectionRowState[] rows = Coordinator.State.ActiveSectionRows
            .Where(row => string.Equals(BuildNavigation.GroupKey(row.Path), _groupKey, StringComparison.Ordinal))
            .ToArray();
        if (rows.Length == 0)
        {
            _body.Add(NativeTheme.Body("Nothing to show here yet.", NativeTheme.Muted));
            return;
        }

        VerticalStackLayout values = new() { Spacing = 13 };
        foreach (SectionRowState row in rows)
        {
            values.Add(NativeTheme.Metric(BuildNavigation.RowLabel(row.Path), row.Value));
        }
        _body.Add(NativeTheme.Card(values));
    }
}

internal static class BuildNavigation
{
    public static string GroupKey(string path)
    {
        string[] segments = (path ?? string.Empty)
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Length > 1 ? segments[0] : "details";
    }

    public static string RowLabel(string path)
    {
        string[] segments = (path ?? string.Empty)
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return RunnerSessionCoordinator.HumanizeId(segments.LastOrDefault() ?? "detail");
    }
}
