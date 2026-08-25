using System.Globalization;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Presentation;
using Chummer.Presentation.Overview;
using Microsoft.Maui.Layouts;

namespace Chummer.Android.Native;

public sealed record BuildPageRouteMarker(string AutomationId, string Label);

public static class BuildPageUiProjection
{
    public static BuildPageRouteMarker RouteMarker(CharacterProfileSection? profile)
        => profile switch
        {
            null => new("phone-runner-empty", "No runner loaded"),
            { Created: false } => new("phone-runner-create", "Creation runner"),
            _ => new("phone-runner-sheet", "Career runner")
        };

    public static string SaveToolbarText(bool hasDurableSaveNotice)
        => hasDurableSaveNotice ? "Saved." : "Save";
}

public sealed class BuildPage : NativePageBase
{
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 16
    };
    private readonly ToolbarItem _save;

    public BuildPage(RunnerSessionCoordinator coordinator) : base(coordinator)
    {
        Title = "Runner";
        AutomationId = "phone-runner-page";
        _save = new ToolbarItem
        {
            Text = "Save",
            AutomationId = "build-save-runner",
            Command = new Command(async () => await RunAsync(() => Coordinator.SaveAsync()))
        };
        ToolbarItems.Add(_save);
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _save.Text = BuildPageUiProjection.SaveToolbarText(Coordinator.HasDurableSaveNotice);
        _save.IsEnabled = Coordinator.State.Profile is not null;
        BuildPageRouteMarker routeMarker = BuildPageUiProjection.RouteMarker(Coordinator.State.Profile);
        AddRouteMarker(routeMarker.AutomationId, routeMarker.Label);
        if (Coordinator.State.Profile is null)
        {
            Title = "Runner";
            _body.Add(NativeTheme.Title("Open a runner first"));
            _body.Add(NativeTheme.Body("Your file stays on this device unless you choose to link it.", NativeTheme.Muted));
            Button open = NativeTheme.PrimaryButton("Open file");
            open.Clicked += async (_, _) => await RunAsync(() => Coordinator.OpenLocalAsync());
            _body.Add(open);
            return;
        }

        if (Coordinator.State.Profile.Created == false)
        {
            Title = "Create";
            AddWorkspacePicker();
            AddCreationWizardDashboard();
            AddFeedback();
            return;
        }

        Title = "Sheet";
        AddWorkspacePicker();
        if (Sr5CareerWizardCatalog.IsSr5CareerRunner(
                Coordinator.State.Profile.Created,
                Coordinator.State.Rules?.GameEdition))
        {
            AddSr5CareerWizardRoute();
        }
        AddSummary();
        AddDossier();
        AddBuildAreas();

        AddFeedback();
    }

    private void AddRouteMarker(string automationId, string label)
    {
        Label marker = NativeTheme.Eyebrow(label);
        marker.AutomationId = automationId;
        _body.Add(marker);
    }

    private void AddSr5CareerWizardRoute()
    {
        VerticalStackLayout card = new() { Spacing = 8 };
        card.Add(NativeTheme.Eyebrow("Shadowrun Fifth Edition"));
        card.Add(NativeTheme.Title("Career", 23));
        card.Add(NativeTheme.Body(
            "Use player-intent journeys and exact review/apply receipts for this created SR5 runner.",
            NativeTheme.Muted));
        card.Add(NativeTheme.NavigationRow(
            "Open Career wizard",
            "Advance · Before Run · Live · After Run · Downtime · Corrections",
            () => Navigation.PushAsync(new Sr5CareerWizardPage(Coordinator)),
            automationId: "build-sr5-career-wizard"));
        card.Add(NativeTheme.NavigationRow(
            "Advance a skill group",
            "Direct deep link · exact InternalId → review → receipt/recovery",
            OpenSr5CareerSkillGroupWizardAsync,
            automationId: "build-career-skill-group"));
        Border route = NativeTheme.Card(card);
        route.AutomationId = Sr5CareerWizardRoutes.Hub;
        _body.Add(route);
    }

    private async Task OpenSr5CareerSkillGroupWizardAsync()
    {
        Sr5CareerSkillGroupCoordinator authority = new(
            new RunnerSessionSr5CareerSkillGroupPresenter(Coordinator),
            new PreferencesSr5CareerCheckpointOwnerAuthority());
        CareerSkillGroupAdvanceEditorState? editor = await authority.PrepareAsync();
        if (editor is not null)
        {
            await Navigation.PushAsync(new Sr5CareerSkillGroupWizardPage(Coordinator, editor));
        }
    }

    private void AddFeedback()
    {
        if (!string.IsNullOrWhiteSpace(Coordinator.State.Error ?? Coordinator.Surface.Error))
        {
            _body.Add(NativeTheme.Body(Coordinator.State.Error ?? Coordinator.Surface.Error!, NativeTheme.Danger));
        }
        else if (!string.IsNullOrWhiteSpace(Coordinator.Notice))
        {
            _body.Add(NativeTheme.Body(Coordinator.Notice!, NativeTheme.Muted));
        }
    }

    private void AddCreationWizardDashboard()
    {
        VerticalStackLayout header = new()
        {
            AutomationId = "creation-wizard-dashboard",
            Spacing = 8
        };
        header.Add(NativeTheme.Eyebrow("Character creation"));
        header.Add(NativeTheme.Title(
            Coordinator.State.Profile?.Alias
            ?? Coordinator.State.Profile?.Name
            ?? "New runner"));
        header.Add(NativeTheme.Body(
            "Build this runner step by step. The full character editor unlocks after creation is complete.",
            NativeTheme.Muted));
        _body.Add(header);

        CharacterCreationWizardSnapshot? snapshot = Coordinator.State.CreationWizard;
        if (snapshot is null)
        {
            Label unavailable = NativeTheme.Body(
                "The authoritative creation projection is unavailable. The wizard is fail-closed and will not "
                + "expose the unrestricted editor or invent rules data.",
                NativeTheme.Danger);
            unavailable.AutomationId = "creation-wizard-snapshot-unavailable";
            _body.Add(NativeTheme.Card(unavailable));
            return;
        }

        Label binding = NativeTheme.Body(
            $"Revision {snapshot.WorkspaceRevision} · snapshot {ShortDigest(snapshot.SnapshotDigest)}",
            NativeTheme.Muted);
        binding.AutomationId = "creation-wizard-binding";
        _body.Add(binding);

        VerticalStackLayout method = new() { Spacing = 8 };
        method.Add(NativeTheme.Eyebrow("Build method"));
        method.Add(NativeTheme.Title(RunnerSessionCoordinator.HumanizeId(snapshot.BuildMethod), 21));
        method.Add(NativeTheme.Metric("Active stage", StageLabel(snapshot, snapshot.ActiveStepId)));
        _body.Add(NativeTheme.Card(method));

        CharacterCreationWizardStageState? lifeModuleStage = snapshot.Steps.FirstOrDefault(candidate =>
            string.Equals(
                candidate.StepId,
                CharacterCreationWizardStepIds.LifeModules,
                StringComparison.Ordinal));
        if (string.Equals(snapshot.BuildMethod, CharacterCreationBuildMethods.LifeModules, StringComparison.Ordinal)
            && (lifeModuleStage is null
                || !lifeModuleStage.IsAvailable
                || lifeModuleStage.Blockers.Count > 0))
        {
            Label blocked = NativeTheme.Body(
                "Life Modules are blocked by the current authoritative projection. Chummer will not substitute or claim "
                + "the Karma workflow. "
                + (lifeModuleStage?.Blockers.FirstOrDefault() ?? "No typed Life Module authority is available."),
                NativeTheme.Danger);
            blocked.AutomationId = "creation-wizard-life-modules-blocked";
            _body.Add(NativeTheme.Card(blocked));
        }

        CharacterCreationFoundationResult<CharacterCreationPrerequisiteState>? prerequisite =
            snapshot.BuildMethod is (CharacterCreationBuildMethods.Priority
                or CharacterCreationBuildMethods.SumToTen)
                ? Coordinator.LoadCreationPrerequisite()
                : null;
        CharacterCreationFoundationResult<CharacterCreationAttributesState>? attributes =
            snapshot.BuildMethod is (CharacterCreationBuildMethods.Priority
                or CharacterCreationBuildMethods.SumToTen)
                ? Coordinator.LoadCreationAttributes()
                : null;
        CharacterCreationFoundationResult<CharacterCreationSkillsState>? skills =
            string.Equals(snapshot.BuildMethod, CharacterCreationBuildMethods.Priority, StringComparison.Ordinal)
                ? Coordinator.LoadCreationSkills()
                : null;
        AddBudgetRibbon(snapshot, attributes, skills);
        AddWizardStages(snapshot, prerequisite, attributes, skills);
        AddCompletionBlockers(snapshot);
        AddLegalNextSteps(snapshot, prerequisite, attributes, skills);
    }

    private void AddBudgetRibbon(
        CharacterCreationWizardSnapshot snapshot,
        CharacterCreationFoundationResult<CharacterCreationAttributesState>? attributes,
        CharacterCreationFoundationResult<CharacterCreationSkillsState>? skills)
    {
        _body.Add(NativeTheme.Eyebrow("Budgets"));
        if (snapshot.Budgets.Count == 0)
        {
            _body.Add(NativeTheme.Body(
                "No authoritative budgets are available. Chummer will not invent a remainder.",
                NativeTheme.Danger));
            return;
        }

        FlexLayout ribbon = new()
        {
            Direction = FlexDirection.Row,
            Wrap = FlexWrap.Wrap,
            JustifyContent = FlexJustify.SpaceBetween,
            AlignItems = FlexAlignItems.Stretch
        };
        foreach (CharacterCreationBudgetState projectedBudget in snapshot.Budgets)
        {
            CharacterCreationBudgetState budget = HasAuthoritativeSkills(skills)
                                                     && skills!.Value is { } skillState
                ? projectedBudget.BudgetId switch
                {
                    "active-skills" => skillState.ActiveSkillPointBudget,
                    "skill-groups" => skillState.SkillGroupPointBudget,
                    "knowledge-skills" => skillState.KnowledgeSkillPointBudget,
                    _ => projectedBudget
                }
                : HasAuthoritativeAttributes(attributes) && attributes!.Value is { } attributeState
                    ? projectedBudget.BudgetId switch
                    {
                        CharacterCreationBudgetIds.NormalAttributes => attributeState.NormalPointBudget,
                        CharacterCreationBudgetIds.SpecialAttributes => attributeState.SpecialPointBudget,
                        CharacterCreationBudgetIds.Karma => attributeState.CreationKarmaBudget,
                        _ => projectedBudget
                    }
                    : projectedBudget;
            string unit = string.IsNullOrWhiteSpace(budget.Unit) ? "points" : budget.Unit;
            VerticalStackLayout card = new()
            {
                MinimumWidthRequest = 164,
                Spacing = 5
            };
            card.Add(NativeTheme.Eyebrow(budget.Label));
            card.Add(NativeTheme.Title(
                budget.IsExact
                    ? $"{budget.Remaining.ToString("0.##", CultureInfo.InvariantCulture)} left"
                    : "Not exact",
                20));
            card.Add(NativeTheme.Body(
                budget.IsExact
                    ? $"{budget.Used.ToString("0.##", CultureInfo.InvariantCulture)} / "
                        + $"{budget.Total.ToString("0.##", CultureInfo.InvariantCulture)} {unit}"
                    : budget.Blockers.FirstOrDefault() ?? "Rules authority unavailable",
                budget.IsExact ? NativeTheme.Muted : NativeTheme.Danger));
            Border budgetCard = NativeTheme.Card(card, new Thickness(14));
            budgetCard.Margin = new Thickness(0, 0, 8, 10);
            budgetCard.AutomationId = $"creation-budget-{Token(budget.BudgetId)}";
            ribbon.Add(budgetCard);
        }
        _body.Add(ribbon);
    }

    private void AddWizardStages(
        CharacterCreationWizardSnapshot snapshot,
        CharacterCreationFoundationResult<CharacterCreationPrerequisiteState>? prerequisite,
        CharacterCreationFoundationResult<CharacterCreationAttributesState>? attributes,
        CharacterCreationFoundationResult<CharacterCreationSkillsState>? skills)
    {
        _body.Add(NativeTheme.Eyebrow("Generation steps"));
        foreach (CharacterCreationWizardStageState stage in snapshot.Steps)
        {
            bool foundation = IsFoundationStage(stage.StepId);
            bool priorityPrerequisite = IsPrerequisiteStage(stage.StepId, snapshot.BuildMethod);
            bool canOpenFoundation = foundation
                                     && stage.IsAvailable
                                     && HasAuthoritativeFoundationOptions();
            bool canOpenPrerequisite = priorityPrerequisite
                                       && stage.IsAvailable
                                       && HasAuthoritativePrerequisiteOptions(prerequisite);
            bool attributeStage = string.Equals(
                stage.StepId,
                CharacterCreationWizardStepIds.Attributes,
                StringComparison.Ordinal);
            bool canOpenAttributes = attributeStage
                                     && HasAuthoritativeAttributes(attributes);
            bool skillStage = string.Equals(stage.StepId, CharacterCreationWizardStepIds.Skills, StringComparison.Ordinal);
            bool canOpenSkills = skillStage && HasAuthoritativeSkills(skills);
            bool canOpen = canOpenFoundation || canOpenPrerequisite || canOpenAttributes || canOpenSkills;
            Func<Task> selected = canOpenPrerequisite
                ? OpenCreationPrerequisiteAsync
                : canOpenAttributes
                    ? OpenCreationAttributesAsync
                : canOpenSkills
                    ? OpenCreationSkillsAsync
                : canOpenFoundation
                    ? OpenCreationFoundationAsync
                    : () => Task.CompletedTask;
            string detail = canOpenPrerequisite
                ? PrerequisiteStageDetail(prerequisite!.Value!)
                : canOpenAttributes
                    ? AttributeStageDetail(attributes!.Value!)
                : canOpenSkills
                    ? SkillsStageDetail(skills!.Value!)
                : canOpenFoundation
                    ? "Choose an exact metatype and Nationality Life Module"
                    : priorityPrerequisite && prerequisite is not null
                        ? prerequisite.Blockers.FirstOrDefault()
                          ?? prerequisite.Value?.Blockers.FirstOrDefault()
                          ?? HumanizeStatus(stage.Status)
                        : HumanizeStatus(stage.Status);
            if (stage.Blockers.Count > 0 && !canOpenAttributes && !canOpenSkills)
            {
                detail += $" · {stage.Blockers[0]}";
            }
            Border row = NativeTheme.NavigationRow(
                stage.Label,
                detail,
                selected,
                enabled: canOpen,
                automationId: $"creation-stage-{Token(stage.StepId)}");
            _body.Add(row);
        }
    }

    private void AddCompletionBlockers(CharacterCreationWizardSnapshot snapshot)
    {
        if (snapshot.CompletionBlockers.Count == 0)
        {
            return;
        }

        VerticalStackLayout blockers = new() { Spacing = 6 };
        blockers.Add(NativeTheme.Eyebrow("Before you can finish"));
        foreach (string blocker in snapshot.CompletionBlockers)
        {
            blockers.Add(NativeTheme.Body($"• {blocker}", NativeTheme.Danger));
        }
        Border card = NativeTheme.Card(blockers);
        card.AutomationId = "creation-wizard-blockers";
        _body.Add(card);
    }

    private void AddLegalNextSteps(
        CharacterCreationWizardSnapshot snapshot,
        CharacterCreationFoundationResult<CharacterCreationPrerequisiteState>? prerequisite,
        CharacterCreationFoundationResult<CharacterCreationAttributesState>? attributeResult,
        CharacterCreationFoundationResult<CharacterCreationSkillsState>? skillsResult)
    {
        CharacterCreationWizardStageState? active = snapshot.Steps.FirstOrDefault(stage =>
            string.Equals(stage.StepId, snapshot.ActiveStepId, StringComparison.Ordinal));
        string[] candidateIds = new[] { snapshot.ActiveStepId }
            .Concat(active?.LegalNextStepIds ?? [])
            .Concat(HasAuthoritativeAttributes(attributeResult)
                ? [CharacterCreationWizardStepIds.Attributes]
                : [])
            .Concat(HasAuthoritativeSkills(skillsResult)
                ? [CharacterCreationWizardStepIds.Skills]
                : [])
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (candidateIds.Length == 0)
        {
            return;
        }

        _body.Add(NativeTheme.Eyebrow("Continue"));
        foreach (string stepId in candidateIds)
        {
            CharacterCreationWizardStageState? stage = snapshot.Steps.FirstOrDefault(candidate =>
                string.Equals(candidate.StepId, stepId, StringComparison.Ordinal));
            if (stage is null)
            {
                continue;
            }

            bool attributeStep = string.Equals(
                stepId,
                CharacterCreationWizardStepIds.Attributes,
                StringComparison.Ordinal);
            bool foundation = IsFoundationStage(stepId);
            bool canOpenFoundation = foundation
                                     && stage.IsAvailable
                                     && HasAuthoritativeFoundationOptions();
            bool canOpenAttributes = attributeStep
                                     && HasAuthoritativeAttributes(attributeResult);
            bool skillStep = string.Equals(stepId, CharacterCreationWizardStepIds.Skills, StringComparison.Ordinal);
            bool canOpenSkills = skillStep && HasAuthoritativeSkills(skillsResult);
            // The post-create AttributeEditRequest path must never serve as a wizard fallback.
            // Core's dedicated creation authority is the only Attributes route here.
            bool canOpen = canOpenFoundation || canOpenAttributes || canOpenSkills;
            Func<Task> selected = canOpenFoundation
                ? OpenCreationFoundationAsync
                : canOpenAttributes
                    ? OpenCreationAttributesAsync
                : canOpenSkills
                    ? OpenCreationSkillsAsync
                : () => Task.CompletedTask;
            string detail = canOpenFoundation
                ? "Choose an exact metatype and Nationality Life Module"
                : canOpenAttributes
                    ? AttributeStageDetail(attributeResult!.Value!)
                : canOpenSkills
                    ? SkillsStageDetail(skillsResult!.Value!)
                : attributeStep && prerequisite?.Value is { } prerequisiteState
                    ? AttributeGateDetail(prerequisiteState)
                : attributeStep && stage.IsAvailable
                    ? "Rules-authoritative Attribute increments and metatype adjustment are not available yet"
                : stage.IsAvailable
                    ? "Legal in the projection · dedicated phone step is not wired yet"
                    : stage.Blockers.FirstOrDefault() ?? "Blocked by the current projection";
            _body.Add(NativeTheme.NavigationRow(
                stage.Label,
                detail,
                selected,
                canOpen,
                $"creation-next-{Token(stepId)}"));
        }
    }

    private bool HasAuthoritativeFoundationOptions()
        => Coordinator.State.Profile?.Created == false
           && Coordinator.State.WorkspaceId is { } workspaceId
           && Coordinator.State.CreationFoundation is { } foundation
           && !foundation.CharacterCreated
           && foundation.Binding.WorkspaceId == workspaceId
           && foundation.Binding.ContentRevision == Coordinator.State.ContentRevision
           && foundation.Binding.SavedRevision == Coordinator.State.SavedRevision
           && foundation.MetatypeOptions.Count > 0
           && foundation.NationalityOptions.Count > 0;

    private bool HasAuthoritativePrerequisiteOptions(
        CharacterCreationFoundationResult<CharacterCreationPrerequisiteState>? result)
        => result is
           {
               Outcome: CharacterCreationFoundationOutcomes.Success,
               Value: { } state
           }
           && CreationPrerequisitePhoneAuthority.IsReady(state, Coordinator.State);

    private bool HasAuthoritativeAttributes(
        CharacterCreationFoundationResult<CharacterCreationAttributesState>? result)
        => result is
           {
               Outcome: CharacterCreationFoundationOutcomes.Success,
               Value: { } state
           }
           && CreationAttributesPhoneAuthority.IsReady(state, Coordinator.State);

    private bool HasAuthoritativeSkills(
        CharacterCreationFoundationResult<CharacterCreationSkillsState>? result)
        => result is { Outcome: CharacterCreationFoundationOutcomes.Success, Value: { } state }
           && CreationSkillsPhoneAuthority.IsReady(state, Coordinator.State);

    private static bool IsPrerequisiteStage(string stepId, string buildMethod)
        => string.Equals(stepId, CharacterCreationWizardStepIds.Method, StringComparison.Ordinal)
           && buildMethod is (CharacterCreationBuildMethods.Priority
               or CharacterCreationBuildMethods.SumToTen);

    private static bool IsFoundationStage(string stepId)
        => string.Equals(stepId, CharacterCreationWizardStepIds.Foundation, StringComparison.Ordinal)
           || string.Equals(stepId, CharacterCreationWizardStepIds.LifeModules, StringComparison.Ordinal);

    private Task OpenCreationFoundationAsync()
        => Navigation.PushAsync(new CreationFoundationPage(Coordinator));

    private Task OpenCreationPrerequisiteAsync()
        => Navigation.PushAsync(new CreationPrerequisitePage(Coordinator));

    private Task OpenCreationAttributesAsync()
        => Navigation.PushAsync(new CreationAttributesPage(Coordinator));

    private Task OpenCreationSkillsAsync()
        => Navigation.PushAsync(new CreationSkillsPage(Coordinator));

    private static string PrerequisiteStageDetail(CharacterCreationPrerequisiteState state)
    {
        string method = string.Equals(
            state.BuildMethod,
            CharacterCreationBuildMethods.SumToTen,
            StringComparison.Ordinal)
            ? "Sum-to-Ten"
            : state.BuildMethod;
        return state.PendingDraft is null
            ? $"Choose five ordered {method} ranks from exact Core authority"
            : $"Resume saved {method} draft · raw Attribute grant "
              + (state.BaseNormalAttributePoints?.ToString(CultureInfo.InvariantCulture) ?? "unavailable");
    }

    private static string AttributeGateDetail(CharacterCreationPrerequisiteState state)
        => state.CanEnterAttributes && !state.RequiresMetatypeAttributeAdjustment
            ? $"Core prerequisite complete · effective normal Attribute grant "
              + (state.EffectiveNormalAttributePoints?.ToString(CultureInfo.InvariantCulture)
                 ?? "unavailable")
              + " · dedicated Creation Attributes phone page not wired yet"
            : $"Raw normal Attribute grant "
              + (state.BaseNormalAttributePoints?.ToString(CultureInfo.InvariantCulture) ?? "not selected")
              + " · Heritage/metatype halveattributepoints adjustment required · Attributes remain disabled";

    private static string AttributeStageDetail(CharacterCreationAttributesState state)
        => state.PendingDraft is null
            ? $"Allocate exact Core ledgers · {state.NormalPointBudget.Remaining.ToString("0.##", CultureInfo.InvariantCulture)} "
              + "normal points left"
            : $"Resume saved Attributes draft {state.PendingDraft.DraftRevision.ToString(CultureInfo.InvariantCulture)} · "
              + $"{state.NormalPointBudget.Remaining.ToString("0.##", CultureInfo.InvariantCulture)} normal points left";

    private static string SkillsStageDetail(CharacterCreationSkillsState state)
        => state.PendingDraft is null
            ? $"Allocate exact Core ledgers · {state.ActiveSkillPointBudget.Remaining.ToString("0.##", CultureInfo.InvariantCulture)} active points left"
            : $"Resume saved Skills draft {state.PendingDraft.DraftRevision.ToString(CultureInfo.InvariantCulture)} · "
              + $"{state.ActiveSkillPointBudget.Remaining.ToString("0.##", CultureInfo.InvariantCulture)} active points left";

    private static string StageLabel(CharacterCreationWizardSnapshot snapshot, string stepId)
        => snapshot.Steps.FirstOrDefault(stage => string.Equals(stage.StepId, stepId, StringComparison.Ordinal))?.Label
            ?? RunnerSessionCoordinator.HumanizeId(stepId);

    private static string HumanizeStatus(string status)
        => RunnerSessionCoordinator.HumanizeId(status);

    private static string ShortDigest(string digest)
        => string.IsNullOrWhiteSpace(digest)
            ? "unavailable"
            : digest[..Math.Min(12, digest.Length)];

    private static string Token(string value)
        => new(value.Trim().ToLowerInvariant().Select(character => char.IsLetterOrDigit(character) ? character : '-').ToArray());

    private void AddDossier()
    {
        _body.Add(NativeTheme.Eyebrow("Runner"));
        _body.Add(NativeTheme.NavigationRow(
            "Origin dossier",
            "Identity, appearance and story",
            () => Navigation.PushAsync(new OriginDossierPage(Coordinator)),
            automationId: "build-origin-dossier"));
        _body.Add(NativeTheme.NavigationRow(
            "Notes",
            "Private notes stored in this runner",
            () => Navigation.PushAsync(new CharacterNotesPage(Coordinator)),
            automationId: "build-character-notes"));
        _body.Add(NativeTheme.NavigationRow(
            "Situational modifiers",
            "Counterspelling dice and active lift/carry hits",
            async () =>
            {
                SituationalModifiersEditorState? editor = await Coordinator.PrepareSituationalModifiersEditAsync();
                if (editor is not null)
                {
                    await Navigation.PushAsync(new SituationalModifiersPage(Coordinator, editor));
                }
            },
            automationId: "build-situational-modifiers"));
        _body.Add(NativeTheme.NavigationRow(
            "Primary arm",
            "Preferred arm or Ambidextrous read-only state",
            async () =>
            {
                PrimaryArmEditorState? editor = await Coordinator.PreparePrimaryArmEditAsync();
                if (editor is not null)
                {
                    await Navigation.PushAsync(new PrimaryArmPage(Coordinator, editor));
                }
            },
            automationId: "build-primary-arm"));
        _body.Add(NativeTheme.NavigationRow(
            "Sustained effects",
            "Edit Psyche, Force, Net Hits, Self-Sustained state, or stop sustaining",
            async () =>
            {
                SustainedObjectsEditorState? editor = await Coordinator.PrepareSustainedObjectsEditAsync();
                if (editor is not null)
                {
                    await Navigation.PushAsync(new SustainedObjectsPage(Coordinator, editor));
                }
            },
            automationId: "build-sustained-effects"));
        _body.Add(NativeTheme.NavigationRow(
            "Group membership",
            "Join or leave a magical group or Resonance network",
            async () =>
            {
                GroupMembershipEditorState? editor = await Coordinator.PrepareGroupMembershipEditAsync();
                if (editor is not null)
                {
                    await Navigation.PushAsync(new GroupMembershipPage(Coordinator, editor));
                }
            },
            automationId: "build-group-membership"));
        _body.Add(NativeTheme.NavigationRow(
            "Group name",
            "Edit the saved initiation group name",
            async () =>
            {
                GroupNameEditorState? editor = await Coordinator.PrepareGroupNameEditAsync();
                if (editor is not null)
                {
                    await Navigation.PushAsync(new GroupNamePage(Coordinator, editor));
                }
            },
            automationId: "build-group-name"));
        _body.Add(NativeTheme.NavigationRow(
            "Tradition name",
            "Edit the saved name of a Custom magical tradition",
            async () =>
            {
                TraditionNameEditorState? editor = await Coordinator.PrepareTraditionNameEditAsync();
                if (editor is not null)
                {
                    await Navigation.PushAsync(new TraditionNamePage(Coordinator, editor));
                }
            },
            automationId: "build-tradition-name"));
        _body.Add(NativeTheme.NavigationRow(
            "Tradition drain",
            "Choose exact drain attributes for an eligible magical tradition",
            async () =>
            {
                TraditionDrainEditorState? editor = await Coordinator.PrepareTraditionDrainEditAsync();
                if (editor is not null)
                {
                    await Navigation.PushAsync(new TraditionDrainPage(Coordinator, editor));
                }
            },
            automationId: "build-tradition-drain"));
        _body.Add(NativeTheme.NavigationRow(
            "Tradition spirits",
            "Edit the five Spirit categories of an exact Custom magical tradition",
            async () =>
            {
                TraditionSpiritCategoryEditorState? editor =
                    await Coordinator.PrepareTraditionSpiritCategoryEditAsync();
                if (editor is not null)
                {
                    await Navigation.PushAsync(new TraditionSpiritCategoryPage(Coordinator, editor));
                }
            },
            automationId: "build-tradition-spirit-categories"));
        _body.Add(NativeTheme.NavigationRow(
            "Convert to Free Sprite",
            "Add Denial and convert an eligible non-Free Sprite",
            async () =>
            {
                FreeSpriteConversionEditorState? editor =
                    await Coordinator.PrepareFreeSpriteConversionAsync();
                if (editor is not null)
                {
                    await Navigation.PushAsync(new FreeSpriteConversionPage(Coordinator, editor));
                }
            },
            automationId: "build-free-sprite-conversion"));
        _body.Add(NativeTheme.NavigationRow(
            "Martial Arts Notes",
            "Edit notes and color for a saved Martial Art or parent-scoped Technique",
            async () =>
            {
                MartialArtNotesEditorState? editor =
                    await Coordinator.PrepareMartialArtNotesEditAsync();
                if (editor is not null)
                {
                    await Navigation.PushAsync(new MartialArtNotesPage(Coordinator, editor));
                }
            },
            automationId: "build-martial-art-notes"));
        _body.Add(NativeTheme.NavigationRow(
            "Delete Martial Art",
            "Delete a saved Martial Art or parent-scoped Technique after explicit confirmation",
            async () =>
            {
                MartialArtDeleteEditorState? editor =
                    await Coordinator.PrepareMartialArtDeleteAsync();
                if (editor is not null)
                {
                    await Navigation.PushAsync(new MartialArtDeletePage(Coordinator, editor));
                }
            },
            automationId: "build-martial-art-delete"));
        if (Coordinator.State.Profile?.Created == false)
        {
            _body.Add(NativeTheme.NavigationRow(
                "Creation Mugshots",
                "Browse existing portraits and choose or clear the exact Main Mugshot",
                async () =>
                {
                    CreationMugshotEditorState? editor = await Coordinator.PrepareCreationMugshotEditAsync();
                    if (editor is not null)
                    {
                        await Navigation.PushAsync(new CreationMugshotPage(Coordinator, editor));
                    }
                },
                automationId: "build-creation-mugshots"));
        }
        if (Coordinator.State.Profile?.Created == true)
        {
            _body.Add(NativeTheme.NavigationRow(
                "Mugshots",
                "Browse existing portraits, choose or clear the exact Main Mugshot, or delete the selected portrait",
                async () =>
                {
                    CareerMugshotEditorState? editor = await Coordinator.PrepareCareerMugshotEditAsync();
                    if (editor is not null)
                    {
                        await Navigation.PushAsync(new CareerMugshotPage(Coordinator, editor));
                    }
                },
                automationId: "build-career-mugshots"));
            _body.Add(NativeTheme.NavigationRow(
                "Improvement groups",
                "Enable or disable every custom Improvement in one saved group",
                async () =>
                {
                    ImprovementGroupActiveEditorState? editor =
                        await Coordinator.PrepareImprovementGroupActiveEditAsync();
                    if (editor is not null)
                    {
                        await Navigation.PushAsync(new ImprovementGroupActivePage(Coordinator, editor));
                    }
                },
                automationId: "build-improvement-group-active"));
            _body.Add(NativeTheme.NavigationRow(
                "Add Improvement Group",
                "Append one exact saved custom Improvement group name",
                async () =>
                {
                    ImprovementGroupAddEditorState? editor =
                        await Coordinator.PrepareImprovementGroupAddAsync();
                    if (editor is not null)
                    {
                        await Navigation.PushAsync(new ImprovementGroupAddPage(Coordinator, editor));
                    }
                },
                automationId: "build-improvement-group-add"));
            _body.Add(NativeTheme.NavigationRow(
                "Improvements",
                "Enable or disable one directly selected saved Improvement",
                async () =>
                {
                    ImprovementActiveEditorState? editor =
                        await Coordinator.PrepareImprovementActiveEditAsync();
                    if (editor is not null)
                    {
                        await Navigation.PushAsync(new ImprovementActivePage(Coordinator, editor));
                    }
                },
                automationId: "build-improvement-active"));
            _body.Add(NativeTheme.NavigationRow(
                "Improvement Notes",
                "Edit notes and note color for one directly selected saved Improvement",
                async () =>
                {
                    ImprovementNotesEditorState? editor =
                        await Coordinator.PrepareImprovementNotesEditAsync();
                    if (editor is not null)
                    {
                        await Navigation.PushAsync(new ImprovementNotesPage(Coordinator, editor));
                    }
                },
                automationId: "build-improvement-notes"));
            _body.Add(NativeTheme.NavigationRow(
                "Edge use",
                "Spend or regain one point of current Edge",
                async () =>
                {
                    CareerEdgeUseEditorState? editor = await Coordinator.PrepareCareerEdgeUseEditAsync();
                    if (editor is not null)
                    {
                        await Navigation.PushAsync(new CareerEdgeUsePage(Coordinator, editor));
                    }
                },
                automationId: "build-career-edge-use"));
            _body.Add(NativeTheme.NavigationRow(
                "Manual Karma",
                "Record dated Karma gained or spent, with optional Nuyen exchange",
                async () =>
                {
                    CareerManualKarmaEditorState? editor = await Coordinator.PrepareCareerManualKarmaEditAsync();
                    if (editor is not null)
                    {
                        await Navigation.PushAsync(new CareerManualKarmaPage(Coordinator, editor));
                    }
                },
                automationId: "build-career-manual-karma"));
            _body.Add(NativeTheme.NavigationRow(
                "Create expense",
                "Choose a source-exact Karma/Nuyen gained or spent operation",
                async () =>
                {
                    await Navigation.PushAsync(new CareerCreateExpenseMenuPage(Coordinator));
                },
                automationId: "build-career-create-expense"));
            _body.Add(NativeTheme.NavigationRow(
                "Manual Nuyen",
                "Record dated Nuyen gained or spent, with percentage and optional Karma exchange",
                async () =>
                {
                    CareerManualNuyenEditorState? editor = await Coordinator.PrepareCareerManualNuyenEditAsync();
                    if (editor is not null)
                    {
                        await Navigation.PushAsync(new CareerManualNuyenPage(Coordinator, editor));
                    }
                },
                automationId: "build-career-manual-nuyen"));
            _body.Add(NativeTheme.NavigationRow(
                "Nuyen expenses",
                "Select a saved Nuyen expense; edit date and reason, and manual-entry amounts",
                async () =>
                {
                    CareerNuyenExpenseEditorState? editor = await Coordinator.PrepareCareerNuyenExpenseEditAsync();
                    if (editor is not null)
                    {
                        await Navigation.PushAsync(new CareerNuyenExpensePage(Coordinator, editor));
                    }
                },
                automationId: "build-career-nuyen-expenses"));
            _body.Add(NativeTheme.NavigationRow(
                "Karma expenses",
                "Select a saved Karma expense; edit date and reason, and source-authorized amounts",
                async () =>
                {
                    CareerKarmaExpenseEditorState? editor = await Coordinator.PrepareCareerKarmaExpenseEditAsync();
                    if (editor is not null)
                    {
                        await Navigation.PushAsync(new CareerKarmaExpensePage(Coordinator, editor));
                    }
                },
                automationId: "build-career-karma-expenses"));
            _body.Add(NativeTheme.NavigationRow(
                "Advance active skill",
                "Review exact Chummer5 rating, maximum and Karma cost, then confirm one atomic advancement",
                async () =>
                {
                    CareerActiveSkillAdvanceEditorState? editor = await Coordinator.PrepareCareerActiveSkillAdvanceAsync();
                    if (editor is not null)
                    {
                        await Navigation.PushAsync(new CareerActiveSkillAdvancePage(Coordinator, editor));
                    }
                },
                automationId: "build-career-active-skill"));
            _body.Add(NativeTheme.NavigationRow(
                "Advance attribute",
                "Choose an exact SR5 attribute quote, review Karma and legality, then apply through a restart-safe receipt checkpoint",
                async () =>
                {
                    Sr5CareerAttributeCoordinator authority = new(
                        new RunnerSessionSr5CareerAttributePresenter(Coordinator),
                        new PreferencesSr5CareerCheckpointOwnerAuthority());
                    CareerAttributeAdvanceEditorState? editor = await authority.PrepareAsync();
                    if (editor is not null)
                    {
                        await Navigation.PushAsync(new Sr5CareerAttributeWizardPage(Coordinator, editor));
                    }
                },
                automationId: "build-career-attribute"));
            _body.Add(NativeTheme.NavigationRow(
                "Calendar",
                "Add the next ISO week, edit its notes and color, or delete it by stable identity",
                async () =>
                {
                    CareerCalendarEditorState? editor = await Coordinator.PrepareCareerCalendarEditAsync();
                    if (editor is not null)
                    {
                        await Navigation.PushAsync(new CareerCalendarPage(Coordinator, editor));
                    }
                },
                automationId: "build-career-calendar"));
            _body.Add(NativeTheme.NavigationRow(
                "Reputation",
                "Street Cred, notoriety and source-aware reputation",
                async () =>
                {
                    CareerReputationEditorState? editor = await Coordinator.PrepareCareerReputationEditAsync();
                    if (editor is not null)
                    {
                        await Navigation.PushAsync(new CareerReputationPage(Coordinator, editor));
                    }
                },
                automationId: "build-career-reputation"));
        }
    }

    private void AddWorkspacePicker()
    {
        IReadOnlyList<OpenWorkspaceState> workspaces = Coordinator.State.OpenWorkspaces;
        if (workspaces.Count <= 1)
        {
            return;
        }

        string[] labels = workspaces.Select(static workspace =>
            !string.IsNullOrWhiteSpace(workspace.Alias) ? workspace.Alias : workspace.Name).ToArray();
        Picker picker = new()
        {
            Title = "Runner",
            ItemsSource = labels,
            SelectedIndex = Math.Max(0, workspaces.ToList().FindIndex(workspace =>
                workspace.Id == Coordinator.State.WorkspaceId)),
            BackgroundColor = NativeTheme.Surface
        };
        picker.SelectedIndexChanged += async (_, _) =>
        {
            if (picker.SelectedIndex >= 0)
            {
                await RunAsync(() => Coordinator.SwitchWorkspaceAsync(workspaces[picker.SelectedIndex]));
            }
        };
        _body.Add(picker);
    }

    private void AddSummary()
    {
        string name = Coordinator.State.Profile?.Alias
            ?? Coordinator.State.Profile?.Name
            ?? "Runner";
        VerticalStackLayout summary = new() { Spacing = 10 };
        summary.Add(NativeTheme.Eyebrow(Coordinator.State.IsDirty ? "Unsaved changes" : "Runner"));
        summary.Add(NativeTheme.Title(name, 24));
        summary.Add(NativeTheme.Metric("Metatype", Coordinator.State.Profile?.Metatype ?? string.Empty));
        summary.Add(NativeTheme.Metric("Metavariant", Coordinator.State.Profile?.Metavariant ?? string.Empty));
        summary.Add(NativeTheme.Metric("Rules", Coordinator.State.Rules?.GameEdition ?? string.Empty));
        summary.Add(NativeTheme.Metric("Character Setting", Coordinator.State.Rules?.Settings ?? string.Empty));
        summary.Add(NativeTheme.Metric("Karma", Coordinator.State.Progress?.Karma.ToString() ?? string.Empty));
        summary.Add(NativeTheme.Metric("Nuyen", Coordinator.State.Progress?.Nuyen.ToString() ?? string.Empty));
        _body.Add(NativeTheme.Card(summary));
    }

    private void AddBuildAreas()
    {
        _body.Add(NativeTheme.Eyebrow("Build areas"));
        foreach (NavigationTabDefinition tab in Coordinator.Surface.NavigationTabs)
        {
            string title = RunnerSessionCoordinator.HumanizeId(tab.Id);
            bool enabled = Coordinator.IsTabEnabled(tab);
            bool active = string.Equals(tab.Id, Coordinator.Surface.ActiveTabId, StringComparison.Ordinal);
            string detail = active && Coordinator.State.ActiveSectionRows.Count > 0
                ? $"{Coordinator.State.ActiveSectionRows.Count} details"
                : "Open section";
            _body.Add(NativeTheme.NavigationRow(
                title,
                detail,
                () => RunAsync(async () =>
                {
                    await Coordinator.SelectTabAsync(tab.Id);
                    await Navigation.PushAsync(new BuildSectionPage(Coordinator, tab.Id, title));
                }),
                enabled,
                $"build-section-{tab.Id}"));
        }
    }

}
