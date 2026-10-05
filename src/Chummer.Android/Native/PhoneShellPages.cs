using Chummer.Presentation.Overview;
using static Chummer.Android.Native.Sr5CareerFlowStrings;

namespace Chummer.Android.Native;

/// <summary>
/// Phone-only runner library host. The existing HomePage remains the tablet Home destination.
/// </summary>
public sealed class RunnersPage : HomePage
{
    public RunnersPage(RunnerSessionCoordinator coordinator)
        : base(
            coordinator,
            PhoneShellRoutes.RunnerAbsolute,
            PhoneStrings.Get("ShellRunners", "Runners"))
    {
        AutomationId = "phone-runners";
    }
}

/// <summary>
/// Private Origin entry for the selected runner, not the unavailable public archive.
/// Listing this destination neither reads provider jobs nor starts book generation.
/// </summary>
public sealed class PhoneStoriesPage : NativePageBase
{
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40), Spacing = 16
    };

    public PhoneStoriesPage(RunnerSessionCoordinator coordinator) : base(coordinator)
    {
        Title = PhoneStrings.Get("ShellStories", "Stories");
        AutomationId = "phone-private-stories";
        Content = new ScrollView { Content = _body };
    }

    protected override void OnAppearing()
    {
        // A cached tab must not flash the previous account's runner before admission.
        _body.Clear();
        base.OnAppearing();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _body.Clear();
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Title(Title));
        var display = Coordinator.State;
        long appearance = CaptureAppearanceGeneration();
        if (Coordinator.CanReadRetainedOriginBook(display))
        {
            string? name = display.Profile?.Alias;
            if (string.IsNullOrWhiteSpace(name)) name = display.Profile?.Name;
            _body.Add(NativeTheme.Title(string.IsNullOrWhiteSpace(name)
                ? PhoneStrings.Get("RunnerFallback", "Runner") : name, 22));
            _body.Add(NativeTheme.Body(PhoneStrings.Get("StoriesPrivateDetail",
                "Read your runner’s Origin chapters and illustrations. Finished chapters can be saved as a book."), NativeTheme.Muted));
            var read = NativeTheme.ReadingButton(AndroidSurfaceStrings.Resolve(
                System.Globalization.CultureInfo.CurrentUICulture.Name)["Origin.ReadBook"]);
            read.AutomationId = "phone-stories-read-book";
            read.Clicked += async (_, _) => await RunAsync(async () =>
            {
                if (IsCurrentAppearanceGeneration(appearance) && Coordinator.CanReadRetainedOriginBook(display))
                    await Navigation.PushAsync(new RetainedOriginBookPage(Coordinator));
            });
            _body.Add(read);
        }
        else
        {
            var message = NativeTheme.Body(PhoneStrings.Get("StoriesChooseRunnerDetail",
                "Open an SR5 Life Modules runner to read its Origin book. Your book stays with that runner."), NativeTheme.Muted);
            message.AutomationId = "phone-stories-choose-runner-message";
            _body.Add(message);
        }
        var choose = NativeTheme.SecondaryButton(PhoneStrings.Get("StoriesChooseRunner", "Choose a runner"));
        choose.AutomationId = "phone-stories-choose-runner";
        choose.Clicked += async (_, _) => await RunAsync(async () =>
        {
            if (IsCurrentAppearanceGeneration(appearance))
                await Shell.Current.GoToAsync(PhoneShellRoutes.RunnersAbsolute);
        });
        _body.Add(choose);
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
/// Phone table-use-case chooser. It exposes only currently typed SR5 authorities: the typed
/// Before Run Edge flow, atomic After Run flow, governed Downtime Calendar flow, and typed
/// Playtime Edge/ammo flow. Missing authorities stay
/// explicit and disabled rather than falling back to generic edits.
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
        Title = Text("Table");
        AutomationId = "phone-table";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow(Text("SR5 Table")));
        _body.Add(NativeTheme.Title(Text("Choose a governed table flow")));
        _body.Add(NativeTheme.Body(
            Text("Each enabled route is backed by a typed Core/Presentation authority and exact saved-runner revision."),
            NativeTheme.Muted));
        bool hasExactSr5CareerAuthority = Coordinator.State.Profile?.Created == true
            && Sr5CareerWizardCatalog.IsSr5CareerRunner(
                characterCreated: true,
                Coordinator.State.Rules?.GameEdition)
            && Coordinator.State.WorkspaceId is not null
            && Coordinator.State.ContentRevision > 0
            && Coordinator.State.SavedRevision == Coordinator.State.ContentRevision
            && !Coordinator.State.IsDirty
            && Coordinator.State.Error is null;

        View beforeRun = NativeTheme.NavigationRow(
            Text("Before Run"),
            Text("Review one exact point of Edge use before the run. Loadout, preparation, contacts, and commitments remain unavailable until they have typed authority."),
            () => Navigation.PushAsync(new Sr5TableWizardPage(
                Coordinator,
                Sr5TableWizardLane.BeforeRun)),
            automationId: "phone-table-before-run");
        beforeRun.IsEnabled = hasExactSr5CareerAuthority;
        _body.Add(beforeRun);
        View afterRun = NativeTheme.NavigationRow(
            Text("After Run"),
            Text("Record local rewards or review a governed run settlement. Resolve any pending transaction first."),
            async () =>
            {
                Sr5AfterRunSettlementCoordinator authority = new(
                    new RunnerSessionSr5AfterRunSettlementPresenter(Coordinator),
                    new PreferencesSr5CareerCheckpointOwnerAuthority());
                Sr5AfterRunSettlementEditorState editor = await authority.PrepareAsync();
                Page destination = await Sr5AfterRunSettlementWizardPage
                    .CreateEntryDestinationAsync(Coordinator, editor);
                await Navigation.PushAsync(destination);
            },
            automationId: "phone-table-after-run");
        afterRun.IsEnabled = hasExactSr5CareerAuthority;
        _body.Add(afterRun);
        AddCapabilityScope("After Run authority", Sr5CareerRunCapabilityCatalog.AfterRun);
        View downtime = NativeTheme.NavigationRow(
            Text("Downtime calendar"),
            Text("Review, confirm and persist one exact SR5 Calendar add, edit or delete with restart recovery"),
            () => Navigation.PushAsync(new Sr5DowntimeCalendarWizardPage(Coordinator)),
            automationId: "phone-table-downtime");
        downtime.IsEnabled = hasExactSr5CareerAuthority;
        _body.Add(downtime);
        View playtime = NativeTheme.NavigationRow(
            Text("Playtime"),
            Text("Quote, review and confirm exact Edge, direct-weapon ammunition, or Physical/Stun damage changes with a restart-safe receipt"),
            () => Navigation.PushAsync(new Sr5TableWizardPage(
                Coordinator,
                Sr5TableWizardLane.Playtime)),
            automationId: "phone-table-playtime");
        playtime.IsEnabled = hasExactSr5CareerAuthority;
        _body.Add(playtime);
        Label blocker = NativeTheme.Body(
            Text("Before Run exposes only typed Edge, and Playtime exposes only typed Edge, direct-weapon ammunition, and Physical/Stun damage tracks. Downtime healing, training, acquisition/install/repair/crafting, lifestyle/contact/project planning, and Playtime temporary modifiers, initiative and run-state remain blocked until composed typed quote/time/receipt authorities exist. No generic mutation fallback is used."),
            NativeTheme.Danger);
        blocker.AutomationId = "phone-table-unavailable-authorities";
        _body.Add(NativeTheme.Card(blocker));
    }

    private void AddCapabilityScope(
        string title,
        IReadOnlyList<Sr5CareerRunCapability> capabilities)
    {
        VerticalStackLayout card = new() { Spacing = 5 };
        card.Add(NativeTheme.Eyebrow(title));
        foreach (Sr5CareerRunCapability capability in capabilities)
        {
            string status = capability.Status switch
            {
                Sr5CareerRunCapabilityStatus.Available => "available",
                Sr5CareerRunCapabilityStatus.ReadOnly => "read-only",
                Sr5CareerRunCapabilityStatus.Unavailable => "unavailable",
                _ => throw new ArgumentOutOfRangeException()
            };
            Label row = NativeTheme.Body(
                $"{capability.Label} · {status} · {capability.Authority}",
                capability.Status == Sr5CareerRunCapabilityStatus.Unavailable
                    ? NativeTheme.Danger
                    : NativeTheme.Muted);
            row.AutomationId = "phone-table-capability-" + capability.Id;
            card.Add(row);
        }
        View border = NativeTheme.Card(card);
        border.AutomationId = "phone-table-after-run-capability-scope";
        _body.Add(border);
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
