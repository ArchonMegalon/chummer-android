using Chummer.Contracts.Presentation;

namespace Chummer.Android.Native;

public sealed class NativeCommandPage : ContentPage
{
    private readonly RunnerSessionCoordinator _coordinator;
    private readonly VerticalStackLayout _commands = new() { Spacing = 9 };
    private string _filter = string.Empty;

    public NativeCommandPage(RunnerSessionCoordinator coordinator)
    {
        _coordinator = coordinator;
        Title = "Actions";
        BackgroundColor = NativeTheme.Paper;
        SearchBar search = new()
        {
            AutomationId = "command-search",
            Placeholder = "Find an action",
            BackgroundColor = NativeTheme.Surface
        };
        search.TextChanged += (_, args) =>
        {
            _filter = args.NewTextValue?.Trim() ?? string.Empty;
            RenderCommands();
        };
        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 12, 20, 36),
            Spacing = 14,
            Children =
            {
                search,
                _commands
            }
        };
        Content = new ScrollView { Content = body };
        RenderCommands();
    }

    private void RenderCommands()
    {
        _commands.Clear();
        AppCommandDefinition[] commands = _coordinator.State.Commands
            .Where(command => string.IsNullOrWhiteSpace(_filter)
                || command.Id.Contains(_filter, StringComparison.OrdinalIgnoreCase)
                || RunnerSessionCoordinator.HumanizeId(command.Id).Contains(_filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(static command => command.Group, StringComparer.Ordinal)
            .ThenBy(static command => command.Id, StringComparer.Ordinal)
            .ToArray();

        if (string.IsNullOrWhiteSpace(_filter))
        {
            foreach (IGrouping<string, AppCommandDefinition> commandGroup in commands.GroupBy(static command => command.Group))
            {
                string groupId = commandGroup.Key;
                AppCommandDefinition[] groupCommands = commandGroup.ToArray();
                _commands.Add(NativeTheme.NavigationRow(
                    RunnerSessionCoordinator.HumanizeId(groupId),
                    groupCommands.Length == 1 ? "1 action" : $"{groupCommands.Length} actions",
                    () => Navigation.PushAsync(new NativeCommandGroupPage(_coordinator, groupId, groupCommands))));
            }
            return;
        }

        string? currentGroup = null;
        foreach (AppCommandDefinition command in commands)
        {
            if (!string.Equals(currentGroup, command.Group, StringComparison.Ordinal))
            {
                currentGroup = command.Group;
                _commands.Add(NativeTheme.Eyebrow(RunnerSessionCoordinator.HumanizeId(currentGroup)));
            }

            Button button = NativeTheme.SecondaryButton(RunnerSessionCoordinator.HumanizeId(command.Id));
            button.AutomationId = $"command-action-{Token(command.Id)}";
            button.IsEnabled = _coordinator.IsCommandEnabled(command);
            button.Clicked += async (_, _) => await ExecuteAsync(command.Id);
            _commands.Add(button);
        }
    }

    private async Task ExecuteAsync(string commandId)
    {
        try
        {
            await _coordinator.ExecuteCommandAsync(commandId);
            if (_coordinator.State.ActiveDialog is { } dialog)
            {
                await Navigation.PushModalAsync(new NavigationPage(new NativeDialogPage(_coordinator, dialog)));
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Chummer", ex.Message, "OK");
        }
    }

    private static string Token(string value)
        => new(value.Trim().ToLowerInvariant().Select(character => char.IsLetterOrDigit(character) ? character : '-').ToArray());
}

internal sealed class NativeCommandGroupPage : NativePageBase
{
    private readonly string _groupId;
    private readonly IReadOnlyList<AppCommandDefinition> _commands;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 10
    };

    public NativeCommandGroupPage(
        RunnerSessionCoordinator coordinator,
        string groupId,
        IReadOnlyList<AppCommandDefinition> commands) : base(coordinator)
    {
        _groupId = groupId;
        _commands = commands;
        Title = RunnerSessionCoordinator.HumanizeId(groupId);
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow("Actions"));
        _body.Add(NativeTheme.Title(RunnerSessionCoordinator.HumanizeId(_groupId), 24));
        foreach (AppCommandDefinition command in _commands)
        {
            Border row = NativeTheme.NavigationRow(
                RunnerSessionCoordinator.HumanizeId(command.Id),
                null,
                () => RunAsync(() => ExecuteAsync(command.Id)),
                Coordinator.IsCommandEnabled(command));
            row.AutomationId = $"command-action-{Token(command.Id)}";
            _body.Add(row);
        }
    }

    private async Task ExecuteAsync(string commandId)
    {
        await Coordinator.ExecuteCommandAsync(commandId);
        if (Coordinator.State.ActiveDialog is { } dialog)
        {
            await Navigation.PushModalAsync(new NavigationPage(new NativeDialogPage(Coordinator, dialog)));
        }
    }

    private static string Token(string value)
        => new(value.Trim().ToLowerInvariant().Select(character => char.IsLetterOrDigit(character) ? character : '-').ToArray());
}
