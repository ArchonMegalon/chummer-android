namespace Chummer.Android.Native;

/// <summary>
/// Phone-only runner library host. The existing HomePage remains the tablet Home destination.
/// </summary>
public sealed class RunnersPage : HomePage
{
    public RunnersPage(RunnerSessionCoordinator coordinator)
        : base(coordinator, PhoneShellRoutes.RunnerAbsolute, "Runners")
    {
        AutomationId = "phone-runners";
    }
}

/// <summary>
/// The current phone candidate has no replayable event-backed Play authority. Keeping the
/// destination fail-closed prevents the former absolute-value scratchpad from implying proof.
/// </summary>
public sealed class PhonePlayPage : NativePageBase
{
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 16
    };

    public PhonePlayPage(RunnerSessionCoordinator coordinator) : base(coordinator)
    {
        Title = "Play";
        AutomationId = "phone-play-unavailable";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow("Phone beta"));
        _body.Add(NativeTheme.Title("Play is not enabled"));
        _body.Add(NativeTheme.Body(
            "This candidate has no proven replayable event overlay. Dice, condition, ammo, effects, and notes "
            + "remain unavailable here instead of being stored as unaudited scratch values.",
            NativeTheme.Muted));
    }
}

/// <summary>
/// The current phone candidate has no proven Before/Live/After/Downtime lifecycle. Existing
/// tablet Campaign tooling remains available only in the postponed tablet composition.
/// </summary>
public sealed class PhoneTablePage : NativePageBase
{
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 16
    };

    public PhoneTablePage(RunnerSessionCoordinator coordinator) : base(coordinator)
    {
        Title = "Table";
        AutomationId = "phone-table-unavailable";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow("Phone beta"));
        _body.Add(NativeTheme.Title("Table is not enabled"));
        _body.Add(NativeTheme.Body(
            "Before Run, Live, After Run, and Downtime need exact role-bound session authority. "
            + "This candidate does not expose campaign or Chronicle tools as a substitute.",
            NativeTheme.Muted));
    }
}

/// <summary>
/// Phone More deliberately omits the generic unrestricted command catalog. Typed lifecycle
/// routes remain the only phone mutation entry points.
/// </summary>
public sealed class PhoneMorePage : MorePage
{
    public PhoneMorePage(RunnerSessionCoordinator coordinator)
        : base(
            coordinator,
            showUnrestrictedActions: false,
            runnerRouteAfterOpen: PhoneShellRoutes.RunnerAbsolute)
    {
        AutomationId = "phone-more";
    }
}
