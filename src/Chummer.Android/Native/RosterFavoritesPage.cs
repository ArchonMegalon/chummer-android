using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class RosterFavoritesPage : NativePageBase
{
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 20, 20, 36),
        Spacing = 16
    };

    public RosterFavoritesPage(RunnerSessionCoordinator coordinator) : base(coordinator)
    {
        Title = "Roster favorite";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        Label marker = NativeTheme.Eyebrow("Character roster");
        marker.AutomationId = "roster-favorites-page";
        _body.Add(marker);
        _body.Add(NativeTheme.Title("Roster metadata"));
        _body.Add(NativeTheme.Body(
            "Matches Chummer5’s Toggle Favorite action. Removing a favorite moves it to the front of Recent.",
            NativeTheme.Muted));

        OpenWorkspaceState? workspace = Coordinator.State.OpenWorkspaces.FirstOrDefault(item =>
            item.Id == Coordinator.State.WorkspaceId);
        if (workspace is null)
        {
            _body.Add(NativeTheme.Body("Open a runner before changing roster metadata.", NativeTheme.Muted));
            return;
        }

        string name = !string.IsNullOrWhiteSpace(workspace.Alias)
            ? workspace.Alias
            : !string.IsNullOrWhiteSpace(workspace.Name) ? workspace.Name : "Runner";
        Switch favorite = new()
        {
            AutomationId = "roster-favorite-toggle",
            IsToggled = Coordinator.IsRosterFavorite(workspace)
        };
        favorite.Toggled += async (_, args) => await RunAsync(() => Coordinator.ToggleRosterFavoriteAsync(
            workspace,
            args.Value,
            Coordinator.RosterFavorites.Revision));

        Grid row = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 12
        };
        VerticalStackLayout labels = new() { Spacing = 3 };
        labels.Add(NativeTheme.Title(name, 20));
        labels.Add(NativeTheme.Body(
            $"Roster metadata revision {Coordinator.RosterFavorites.Revision}",
            NativeTheme.Muted));
        row.Add(labels);
        row.Add(favorite, 1);
        _body.Add(NativeTheme.Card(row));

        _body.Add(NativeTheme.Title("Sort roster lists", 20));
        _body.Add(NativeTheme.Body(
            "Matches Chummer5’s Sort action for the selected Favorite or Recent collection.",
            NativeTheme.Muted));

        Button sortFavorites = NativeTheme.SecondaryButton("Sort favorites");
        sortFavorites.AutomationId = "roster-sort-favorites";
        sortFavorites.Clicked += async (_, _) => await RunAsync(() => Coordinator.SortRosterAsync(
            Chummer.Contracts.Api.CharacterRosterSortTarget.Favorites,
            Coordinator.RosterFavorites.Revision));
        _body.Add(sortFavorites);

        Button sortRecent = NativeTheme.SecondaryButton("Sort recent");
        sortRecent.AutomationId = "roster-sort-recent";
        sortRecent.Clicked += async (_, _) => await RunAsync(() => Coordinator.SortRosterAsync(
            Chummer.Contracts.Api.CharacterRosterSortTarget.Recent,
            Coordinator.RosterFavorites.Revision));
        _body.Add(sortRecent);
    }
}
