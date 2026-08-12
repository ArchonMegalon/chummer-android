using Chummer.Android.Platform;

namespace Chummer.Android.Native;

public sealed class PlayPage : NativePageBase
{
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 16
    };

    public PlayPage(RunnerSessionCoordinator coordinator) : base(coordinator)
    {
        Title = "Play";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow("At the table"));
        _body.Add(NativeTheme.Title("Play"));

        if (Coordinator.State.Profile is null)
        {
            _body.Add(NativeTheme.Body("Open a runner to start.", NativeTheme.Muted));
            Button home = NativeTheme.PrimaryButton("Choose runner");
            home.Clicked += async (_, _) => await Shell.Current.GoToAsync("//home");
            _body.Add(home);
            return;
        }

        AddContext();
        AddDice();
        AddCondition();
        AddNotes();
    }

    private void AddContext()
    {
        VerticalStackLayout context = new() { Spacing = 10 };
        string runner = Coordinator.State.Profile?.Alias
            ?? Coordinator.State.Profile?.Name
            ?? "Runner";
        context.Add(NativeTheme.Eyebrow("Loaded runner"));
        context.Add(NativeTheme.Title(runner, 22));

        if (Coordinator.Groups.Count > 0)
        {
            string[] names = Coordinator.Groups.Select(static group => group.Name).ToArray();
            AndroidLinkedGroup? selected = Coordinator.SelectedGroup;
            Picker groups = new()
            {
                Title = "Campaign",
                ItemsSource = names,
                SelectedIndex = Math.Max(0, Coordinator.Groups.ToList().FindIndex(group =>
                    string.Equals(group.GroupId, selected?.GroupId, StringComparison.Ordinal))),
                BackgroundColor = NativeTheme.Surface
            };
            groups.SelectedIndexChanged += (_, _) =>
            {
                if (groups.SelectedIndex >= 0)
                {
                    Coordinator.SelectGroup(Coordinator.Groups[groups.SelectedIndex]);
                }
            };
            context.Add(groups);
        }
        else if (Coordinator.Account.IsLinked)
        {
            Button load = NativeTheme.SecondaryButton("Load campaigns");
            load.Clicked += async (_, _) => await RunAsync(() => Coordinator.RefreshLinkedDataAsync());
            context.Add(load);
        }

        _body.Add(NativeTheme.Card(context));
    }

    private void AddDice()
    {
        NativePlaySnapshot play = Coordinator.Play;
        VerticalStackLayout dice = new() { Spacing = 12 };
        dice.Add(NativeTheme.Eyebrow("Dice"));
        Grid pool = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 12
        };
        Stepper stepper = new(1, 100, play.LastPool, 1);
        Label poolValue = NativeTheme.Title(play.LastPool.ToString(), 24);
        stepper.ValueChanged += (_, args) => poolValue.Text = ((int)args.NewValue).ToString();
        VerticalStackLayout poolControl = new()
        {
            Spacing = 3,
            Children = { NativeTheme.Body("Pool", NativeTheme.Muted), poolValue }
        };
        pool.Add(poolControl);
        pool.Add(stepper, 1);
        dice.Add(pool);

        Button roll = NativeTheme.PrimaryButton("Roll");
        roll.Clicked += (_, _) => Coordinator.RollDice((int)stepper.Value);
        dice.Add(roll);

        if (play.LastRoll.Count > 0)
        {
            string result = play.Glitch
                ? $"{play.Hits} hits · glitch"
                : $"{play.Hits} hits";
            dice.Add(NativeTheme.Title(result, 20));
            dice.Add(NativeTheme.Body(string.Join("  ", play.LastRoll), NativeTheme.Muted));
        }

        Button fullRoller = NativeTheme.SecondaryButton("Advanced dice roller");
        fullRoller.Clicked += async (_, _) => await RunAsync(() => Coordinator.ExecuteCommandAsync("dice_roller"));
        dice.Add(fullRoller);
        _body.Add(NativeTheme.Card(dice));
    }

    private void AddCondition()
    {
        NativePlaySnapshot play = Coordinator.Play;
        VerticalStackLayout condition = new() { Spacing = 12 };
        condition.Add(NativeTheme.Eyebrow("Condition"));
        condition.Add(CreateDamageControl("Physical", play.PhysicalDamage, value =>
            Coordinator.SetDamage(value, Coordinator.Play.StunDamage)));
        condition.Add(CreateDamageControl("Stun", play.StunDamage, value =>
            Coordinator.SetDamage(Coordinator.Play.PhysicalDamage, value)));
        condition.Add(NativeTheme.Body("Table marks stay with this runner and campaign on this device.", NativeTheme.Muted));
        _body.Add(NativeTheme.Card(condition));
    }

    private static View CreateDamageControl(string label, int value, Action<int> changed)
    {
        Grid grid = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 12
        };
        Label valueLabel = NativeTheme.Body($"{label}  {value}");
        valueLabel.FontAttributes = FontAttributes.Bold;
        Stepper stepper = new(0, 18, value, 1);
        stepper.ValueChanged += (_, args) =>
        {
            int current = (int)args.NewValue;
            valueLabel.Text = $"{label}  {current}";
            changed(current);
        };
        grid.Add(valueLabel);
        grid.Add(stepper, 1);
        return grid;
    }

    private void AddNotes()
    {
        VerticalStackLayout notes = new() { Spacing = 10 };
        notes.Add(NativeTheme.Eyebrow("Session notes"));
        Editor editor = new()
        {
            Text = Coordinator.Play.Notes,
            Placeholder = "Quick notes for this table",
            MinimumHeightRequest = 120,
            AutoSize = EditorAutoSizeOption.TextChanges,
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        editor.Unfocused += (_, _) => Coordinator.SetPlayNotes(editor.Text);
        notes.Add(editor);
        _body.Add(NativeTheme.Card(notes));
    }
}
