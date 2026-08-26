using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class Sr5CareerWizardPage : NativePageBase
{
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };

    public Sr5CareerWizardPage(RunnerSessionCoordinator coordinator) : base(coordinator)
    {
        Title = "SR5 Career";
        AutomationId = Sr5CareerWizardRoutes.Hub;
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow("Shadowrun Fifth Edition"));
        _body.Add(NativeTheme.Title("Career wizard"));

        bool ready = Sr5CareerWizardCatalog.IsSr5CareerRunner(
            Coordinator.State.Profile?.Created == true,
            Coordinator.State.Rules?.GameEdition);
        if (!ready)
        {
            Label blocker = NativeTheme.Body(
                "This wizard is fail-closed. Open a created SR5 runner; creation characters and other rules editions are not accepted.",
                NativeTheme.Danger);
            blocker.AutomationId = "sr5-career-wizard-edition-blocker";
            _body.Add(NativeTheme.Card(blocker));
            return;
        }

        _body.Add(NativeTheme.Body(
            "Choose a player-intent lane. Every enabled action enters its own typed authority; unavailable families stay visible and blocked instead of falling through to generic All actions.",
            NativeTheme.Muted));
        Label binding = NativeTheme.Body(
            $"Workspace {Coordinator.State.WorkspaceId?.Value} · revision {Coordinator.State.ContentRevision} · saved {Coordinator.State.SavedRevision}",
            NativeTheme.Muted);
        binding.AutomationId = "sr5-career-wizard-binding";
        _body.Add(binding);

        foreach (Sr5CareerWizardLaneDefinition definition in Sr5CareerWizardCatalog.Lanes)
        {
            string status = definition.Availability switch
            {
                Sr5CareerWizardAvailability.Available => "Available",
                Sr5CareerWizardAvailability.Partial => "Partial · exact actions only",
                _ => "Blocked"
            };
            _body.Add(NativeTheme.NavigationRow(
                definition.Title,
                $"{status} · {definition.Summary}",
                () => Navigation.PushAsync(new Sr5CareerJourneyPage(Coordinator, definition)),
                definition.Availability != Sr5CareerWizardAvailability.Blocked,
                Sr5CareerWizardRoutes.Lane(definition.Lane)));
        }

        Label boundary = NativeTheme.Body(
            "The shared typed CostQuote → CareerActionPlan → atomic ApplyResult boundary is proven independently for Active Skill and Attribute actions. Multi-action plans remain blocked until Core publishes an atomic bundle contract.",
            NativeTheme.Muted);
        boundary.AutomationId = "sr5-career-wizard-transaction-boundary";
        _body.Add(NativeTheme.Card(boundary));
    }

    internal static string LaneToken(Sr5CareerWizardLane lane)
        => lane switch
        {
            Sr5CareerWizardLane.Advancement => "advancement",
            Sr5CareerWizardLane.BeforeRun => "before-run",
            Sr5CareerWizardLane.LiveRun => "live-run",
            Sr5CareerWizardLane.AfterRun => "after-run",
            Sr5CareerWizardLane.Downtime => "downtime",
            Sr5CareerWizardLane.Corrections => "corrections",
            _ => throw new ArgumentOutOfRangeException(nameof(lane))
        };
}

public sealed class Sr5CareerJourneyPage : NativePageBase
{
    private readonly Sr5CareerWizardLaneDefinition _definition;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };

    public Sr5CareerJourneyPage(
        RunnerSessionCoordinator coordinator,
        Sr5CareerWizardLaneDefinition definition) : base(coordinator)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        Title = definition.Title;
        AutomationId = Sr5CareerWizardRoutes.Lane(definition.Lane);
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow("SR5 Career"));
        _body.Add(NativeTheme.Title(_definition.Title));
        _body.Add(NativeTheme.Body(_definition.Summary, NativeTheme.Muted));

        bool ready = Sr5CareerWizardCatalog.IsSr5CareerRunner(
            Coordinator.State.Profile?.Created == true,
            Coordinator.State.Rules?.GameEdition);
        if (!ready)
        {
            AddBlocked("This runner is no longer a created SR5 runner. Reopen the Career wizard.", "edition");
            return;
        }

        switch (_definition.Lane)
        {
            case Sr5CareerWizardLane.Advancement:
                AddAdvancement();
                break;
            case Sr5CareerWizardLane.BeforeRun:
                AddBeforeRun();
                break;
            case Sr5CareerWizardLane.LiveRun:
                AddLiveRun();
                break;
            case Sr5CareerWizardLane.AfterRun:
                AddAfterRun();
                break;
            case Sr5CareerWizardLane.Downtime:
                AddDowntime();
                break;
            case Sr5CareerWizardLane.Corrections:
                AddCorrections();
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        Label authority = NativeTheme.Body(_definition.AuthorityNote, NativeTheme.Muted);
        authority.AutomationId = "sr5-career-journey-authority-note";
        _body.Add(NativeTheme.Card(authority));
    }

    private void AddAdvancement()
    {
        AddAction(
            "Advance an active skill",
            "Choose → exact SR5 quote → review diff → durable apply → receipt",
            OpenActiveSkillWizardAsync,
            "active-skill");
        AddAction(
            "Advance an attribute",
            "Choose exact Core quote → preview Karma and legality → durable apply → recovered receipt",
            OpenAttributeWizardAsync,
            "attribute");
        AddBlocked(
            "Skill-group and specialization authorities exist upstream, but Android needs shared Coordinator routing and an atomic ApplyResult. Knowledge skills, qualities and initiation/submersion remain incomplete.",
            "other-advancement");
        AddBlocked(
            "Gear, weapon, armor, bioware, vehicle and general ware acquisition need exact availability, cost, Essence, prerequisite and expense quotes.",
            "commerce");
    }

    private void AddBeforeRun()
    {
        AddAction(
            "Review current Edge",
            "Spend or regain exactly one point through the revision-bound Career authority",
            OpenEdgeAsync,
            "edge");
        AddBlocked(
            "A typed pre-run checklist for healing, loadout, ammunition, contacts, identities and licenses is not available.",
            "checklist");
    }

    private void AddLiveRun()
    {
        AddAction(
            "Use Edge",
            "Spend or regain one saved point without opening unrestricted editing",
            OpenEdgeAsync,
            "edge");
        AddBlocked(
            "Weapon fire is exact only from a stable selected weapon context; the hub will not guess a weapon identity.",
            "weapon-fire");
        AddBlocked(
            "Dice and quick table notes are local Play state, not a Career transaction receipt.",
            "play-state");
    }

    private void AddAfterRun()
    {
        AddAction(
            "Record Karma",
            "One dated typed Karma gain or spend; saved independently",
            OpenManualKarmaAsync,
            "karma");
        AddAction(
            "Record Nuyen",
            "One dated typed Nuyen gain or spend; saved independently",
            OpenManualNuyenAsync,
            "nuyen");
        AddAction(
            "Update reputation",
            "Street Cred, Notoriety, Public Awareness and source-gated reputation",
            OpenReputationAsync,
            "reputation");
        AddBlocked(
            "There is no Heat field in the current typed SR5 authority and no typed After Run contact transaction.",
            "heat-contacts");
        AddBlocked(
            "Karma, Nuyen and reputation cannot be reviewed or committed as one atomic run-closeout bundle yet.",
            "atomic-closeout");
    }

    private void AddDowntime()
    {
        AddAction(
            "Plan calendar weeks",
            "Add, edit or delete exact saved ISO weeks by stable identity",
            OpenCalendarAsync,
            "calendar");
        AddAction(
            "Advance an active skill",
            "Execute one exact advancement through the reviewed transaction boundary",
            OpenActiveSkillWizardAsync,
            "active-skill");
        AddAction(
            "Advance an attribute",
            "Preview and save one exact SR5 attribute advancement; elapsed time remains Chummer5's immediate persistence authority",
            OpenAttributeWizardAsync,
            "attribute");
        AddBlocked(
            "Training duration, healing, crafting, acquisition delivery and other scheduled work lack a shared typed execution contract.",
            "execution");
    }

    private void AddCorrections()
    {
        AddAction(
            "Edit Karma expense",
            "Edit only Chummer5-authorized fields on one stable saved entry",
            OpenKarmaExpensesAsync,
            "karma-expense");
        AddAction(
            "Edit Nuyen expense",
            "Edit only Chummer5-authorized fields on one stable saved entry",
            OpenNuyenExpensesAsync,
            "nuyen-expense");
        AddBlocked(
            "Undo Karma/Nuyen Expense and Correct this transaction are not typed on Android yet.",
            "undo");
        AddBlocked(
            "Active-skill and attribute reviewed drafts survive restart, and applying drafts fail closed against replay. Shared rebase/discard and recovery for every other action are not implemented.",
            "recovery");
    }

    private void AddAction(
        string title,
        string detail,
        Func<Task> selected,
        string token)
        => _body.Add(NativeTheme.NavigationRow(
            title,
            detail,
            selected,
            automationId: $"sr5-career-action-{token}"));

    private void AddBlocked(string reason, string token)
    {
        Button blocked = NativeTheme.SecondaryButton("Unavailable");
        blocked.AutomationId = $"sr5-career-blocked-{token}";
        blocked.IsEnabled = false;
        VerticalStackLayout content = new() { Spacing = 7 };
        content.Add(blocked);
        content.Add(NativeTheme.Body(reason, NativeTheme.Danger));
        _body.Add(NativeTheme.Card(content));
    }

    private async Task OpenActiveSkillWizardAsync()
    {
        Sr5CareerActiveSkillCoordinator authority = new(
            new RunnerSessionSr5CareerActiveSkillPresenter(Coordinator),
            new PreferencesSr5CareerCheckpointOwnerAuthority());
        CareerActiveSkillAdvanceEditorState? editor =
            await authority.PrepareAsync();
        if (editor is not null)
        {
            await Navigation.PushAsync(new Sr5CareerActiveSkillWizardPage(Coordinator, editor));
        }
    }

    private async Task OpenAttributeWizardAsync()
    {
        Sr5CareerAttributeCoordinator authority = new(
            new RunnerSessionSr5CareerAttributePresenter(Coordinator),
            new PreferencesSr5CareerCheckpointOwnerAuthority());
        CareerAttributeAdvanceEditorState? editor = await authority.PrepareAsync();
        if (editor is not null)
        {
            await Navigation.PushAsync(new Sr5CareerAttributeWizardPage(Coordinator, editor));
        }
    }

    private async Task OpenEdgeAsync()
    {
        CareerEdgeUseEditorState? editor = await Coordinator.PrepareCareerEdgeUseEditAsync();
        if (editor is not null)
        {
            await Navigation.PushAsync(new CareerEdgeUsePage(Coordinator, editor));
        }
    }

    private async Task OpenManualKarmaAsync()
    {
        CareerManualKarmaEditorState? editor = await Coordinator.PrepareCareerManualKarmaEditAsync();
        if (editor is not null)
        {
            await Navigation.PushAsync(new CareerManualKarmaPage(Coordinator, editor));
        }
    }

    private async Task OpenManualNuyenAsync()
    {
        CareerManualNuyenEditorState? editor = await Coordinator.PrepareCareerManualNuyenEditAsync();
        if (editor is not null)
        {
            await Navigation.PushAsync(new CareerManualNuyenPage(Coordinator, editor));
        }
    }

    private async Task OpenReputationAsync()
    {
        CareerReputationEditorState? editor = await Coordinator.PrepareCareerReputationEditAsync();
        if (editor is not null)
        {
            await Navigation.PushAsync(new CareerReputationPage(Coordinator, editor));
        }
    }

    private async Task OpenCalendarAsync()
    {
        CareerCalendarEditorState? editor = await Coordinator.PrepareCareerCalendarEditAsync();
        if (editor is not null)
        {
            await Navigation.PushAsync(new CareerCalendarPage(Coordinator, editor));
        }
    }

    private async Task OpenKarmaExpensesAsync()
    {
        CareerKarmaExpenseEditorState? editor = await Coordinator.PrepareCareerKarmaExpenseEditAsync();
        if (editor is not null)
        {
            await Navigation.PushAsync(new CareerKarmaExpensePage(Coordinator, editor));
        }
    }

    private async Task OpenNuyenExpensesAsync()
    {
        CareerNuyenExpenseEditorState? editor = await Coordinator.PrepareCareerNuyenExpenseEditAsync();
        if (editor is not null)
        {
            await Navigation.PushAsync(new CareerNuyenExpensePage(Coordinator, editor));
        }
    }
}
