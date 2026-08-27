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
/// Phone table-use-case chooser. It exposes only currently typed SR5 authorities: the existing
/// atomic After Run flow and the governed Downtime Calendar flow. Missing authorities stay
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
        Title = "Table";
        AutomationId = "phone-table";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow("SR5 Table"));
        _body.Add(NativeTheme.Title("Choose a governed table flow"));
        _body.Add(NativeTheme.Body(
            "Each enabled route is backed by a typed Core/Presentation authority and exact saved-runner revision.",
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

        Button beforeRun = NativeTheme.SecondaryButton("Before Run (unavailable)");
        beforeRun.AutomationId = "phone-table-before-run-unavailable";
        beforeRun.IsEnabled = false;
        _body.Add(beforeRun);
        View afterRun = NativeTheme.NavigationRow(
            "After Run",
            "Settle governed rewards, Heat/reputation, contacts, approvals and the atomic Core receipt",
            async () =>
            {
                Sr5AfterRunSettlementCoordinator authority = new(
                    new RunnerSessionSr5AfterRunSettlementPresenter(Coordinator),
                    new PreferencesSr5CareerCheckpointOwnerAuthority());
                Sr5AfterRunSettlementEditorState editor = await authority.PrepareAsync();
                Page destination = editor.Status == Sr5AfterRunCatalogStatus.Missing
                    && Coordinator.SupportsManualAfterRunProposalEntry
                        ? new Sr5AfterRunManualProposalPage(
                            Coordinator,
                            editor.WorkspaceId,
                            editor.WorkspaceRevision)
                        : new Sr5AfterRunSettlementWizardPage(Coordinator, editor);
                await Navigation.PushAsync(destination);
            },
            automationId: "phone-table-after-run");
        afterRun.IsEnabled = hasExactSr5CareerAuthority;
        _body.Add(afterRun);
        View downtime = NativeTheme.NavigationRow(
            "Downtime calendar",
            "Review, confirm and persist one exact SR5 Calendar add, edit or delete with restart recovery",
            () => Navigation.PushAsync(new Sr5DowntimeCalendarWizardPage(Coordinator)),
            automationId: "phone-table-downtime");
        downtime.IsEnabled = hasExactSr5CareerAuthority;
        _body.Add(downtime);
        Button playtime = NativeTheme.SecondaryButton("Playtime (unavailable)");
        playtime.AutomationId = "phone-table-playtime-unavailable";
        playtime.IsEnabled = false;
        _body.Add(playtime);
        Label blocker = NativeTheme.Body(
            "Before Run and Playtime remain unavailable until Core exposes replayable typed authorities. No generic mutation fallback is used.",
            NativeTheme.Danger);
        blocker.AutomationId = "phone-table-unavailable-authorities";
        _body.Add(NativeTheme.Card(blocker));
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
