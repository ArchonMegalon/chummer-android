using Chummer.Presentation.Overview;

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

        View beforeRun = NativeTheme.NavigationRow(
            "Before Run",
            "Choose one revision-bound Edge preparation action; other preparation remains explicitly blocked",
            () => Navigation.PushAsync(new Sr5TableWizardPage(
                Coordinator,
                Sr5TableWizardLane.BeforeRun)),
            automationId: "phone-table-before-run");
        beforeRun.IsEnabled = hasExactSr5CareerAuthority;
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
        AddCapabilityScope("After Run authority", Sr5CareerRunCapabilityCatalog.AfterRun);
        View downtime = NativeTheme.NavigationRow(
            "Downtime calendar",
            "Review, confirm and persist one exact SR5 Calendar add, edit or delete with restart recovery",
            () => Navigation.PushAsync(new Sr5DowntimeCalendarWizardPage(Coordinator)),
            automationId: "phone-table-downtime");
        downtime.IsEnabled = hasExactSr5CareerAuthority;
        _body.Add(downtime);
        View playtime = NativeTheme.NavigationRow(
            "Playtime",
            "Quote, review and confirm exact Edge or direct-weapon ammunition changes with a restart-safe receipt",
            () => Navigation.PushAsync(new Sr5TableWizardPage(
                Coordinator,
                Sr5TableWizardLane.Playtime)),
            automationId: "phone-table-playtime");
        playtime.IsEnabled = hasExactSr5CareerAuthority;
        _body.Add(playtime);
        Label blocker = NativeTheme.Body(
            "Before Run exposes only typed Edge, and Playtime exposes only typed Edge and direct-weapon ammunition. Downtime healing, training, acquisition/install/repair/crafting, lifestyle/contact/project planning, and Playtime damage/conditions, temporary modifiers, initiative and run-state remain blocked until composed typed quote/time/receipt authorities exist. No generic mutation fallback is used.",
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
