using Chummer.Android.Platform;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class HomePage : NativePageBase
{
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 20, 20, 36),
        Spacing = 18
    };

    public HomePage(RunnerSessionCoordinator coordinator) : base(coordinator)
    {
        Title = "Home";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow("Chummer"));
        _body.Add(NativeTheme.Title("Your runners"));

        string runner = Coordinator.State.Profile?.Alias
            ?? Coordinator.State.Profile?.Name
            ?? "No runner open";
        string detail = Coordinator.State.Profile is null
            ? "Open a file, link your account, or start a runner."
            : string.Join(" · ", new[]
            {
                Coordinator.State.Profile.Metatype,
                Coordinator.State.Rules?.GameEdition
            }.Where(static value => !string.IsNullOrWhiteSpace(value)));

        VerticalStackLayout current = new() { Spacing = 9 };
        current.Add(NativeTheme.Eyebrow("Current"));
        current.Add(NativeTheme.Title(runner, 22));
        current.Add(NativeTheme.Body(detail, NativeTheme.Muted));
        if (Coordinator.State.Profile is not null)
        {
            Button continueButton = NativeTheme.PrimaryButton("Continue building");
            continueButton.Clicked += async (_, _) => await Shell.Current.GoToAsync("//build");
            current.Add(continueButton);
        }
        _body.Add(NativeTheme.Card(current));

        Grid quick = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 10,
            RowSpacing = 10
        };
        Button open = NativeTheme.PrimaryButton("Open file");
        open.AutomationId = "home-open-file";
        open.Clicked += async (_, _) => await RunAsync(() => Coordinator.OpenLocalAsync());
        Button create = NativeTheme.SecondaryButton("New runner");
        create.AutomationId = "home-new-runner";
        create.Clicked += async (_, _) => await RunAsync(() => Coordinator.CreateRunnerAsync());
        quick.Add(open);
        quick.Add(create, 1);
        _body.Add(quick);

        if (Coordinator.State.OpenWorkspaces.Count > 1)
        {
            _body.Add(NativeTheme.Eyebrow("Open now"));
            foreach (OpenWorkspaceState workspace in Coordinator.State.OpenWorkspaces.Take(5))
            {
                string label = !string.IsNullOrWhiteSpace(workspace.Alias) ? workspace.Alias : workspace.Name;
                Button button = NativeTheme.SecondaryButton(string.IsNullOrWhiteSpace(label) ? "Runner" : label);
                button.Clicked += async (_, _) => await RunAsync(() => Coordinator.SwitchWorkspaceAsync(workspace));
                _body.Add(button);
            }
        }

        AddOnlineSection();
        if (!string.IsNullOrWhiteSpace(Coordinator.Notice))
        {
            _body.Add(NativeTheme.Body(Coordinator.Notice, NativeTheme.Muted));
        }
    }

    private void AddOnlineSection()
    {
        VerticalStackLayout online = new() { Spacing = 10 };
        online.Add(NativeTheme.Eyebrow("Chummer.run"));
        online.Add(NativeTheme.Title(Coordinator.Account.IsLinked ? "Online runners" : "Link your account", 21));

        if (!Coordinator.Account.IsLinked)
        {
            online.Add(NativeTheme.Body("Open runners saved to your Chummer account.", NativeTheme.Muted));
            Button link = NativeTheme.PrimaryButton("Link account");
            link.Clicked += async (_, _) => await RunAsync(() => Coordinator.BeginAccountLinkAsync());
            online.Add(link);
        }
        else
        {
            Button refresh = NativeTheme.SecondaryButton(
                Coordinator.OnlineCharacters.Count == 0 ? "Load online runners" : "Refresh");
            refresh.Clicked += async (_, _) => await RunAsync(() => Coordinator.RefreshLinkedDataAsync());
            online.Add(refresh);
            foreach (AndroidOnlineCharacter character in Coordinator.OnlineCharacters.Take(6))
            {
                string name = !string.IsNullOrWhiteSpace(character.Alias)
                    ? character.Alias
                    : !string.IsNullOrWhiteSpace(character.Name) ? character.Name : "Runner";
                Button button = NativeTheme.SecondaryButton(name);
                button.Clicked += async (_, _) => await RunAsync(async () =>
                {
                    await Coordinator.OpenOnlineAsync(character);
                    await Shell.Current.GoToAsync("//build");
                });
                online.Add(button);
            }
        }

        _body.Add(NativeTheme.Card(online));
    }
}
