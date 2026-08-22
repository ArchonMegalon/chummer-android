using Chummer.Contracts.Presentation;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

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
        Title = "Build";
        _save = new ToolbarItem
        {
            Text = "Save",
            AutomationId = "build-save-runner",
            Command = new Command(async () => await RunAsync(() => Coordinator.SaveAsync()))
        };
        ToolbarItems.Add(_save);
        ToolbarItems.Add(new ToolbarItem
        {
            Text = "Actions",
            Order = ToolbarItemOrder.Primary,
            Priority = 1,
            Command = new Command(async () => await Navigation.PushAsync(new NativeCommandPage(Coordinator)))
        });
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _save.IsEnabled = Coordinator.State.Profile is not null;
        if (Coordinator.State.Profile is null)
        {
            _body.Add(NativeTheme.Eyebrow("Build"));
            _body.Add(NativeTheme.Title("Open a runner first"));
            _body.Add(NativeTheme.Body("Your file stays on this device unless you choose to link it.", NativeTheme.Muted));
            Button open = NativeTheme.PrimaryButton("Open file");
            open.Clicked += async (_, _) => await RunAsync(() => Coordinator.OpenLocalAsync());
            _body.Add(open);
            return;
        }

        AddWorkspacePicker();
        AddSummary();
        AddDossier();
        AddBuildAreas();
        AddTools();

        if (!string.IsNullOrWhiteSpace(Coordinator.State.Error ?? Coordinator.Surface.Error))
        {
            _body.Add(NativeTheme.Body(Coordinator.State.Error ?? Coordinator.Surface.Error!, NativeTheme.Danger));
        }
        else if (!string.IsNullOrWhiteSpace(Coordinator.Notice))
        {
            _body.Add(NativeTheme.Body(Coordinator.Notice!, NativeTheme.Muted));
        }
    }

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
        if (Coordinator.State.Profile?.Created == true)
        {
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

    private void AddTools()
    {
        _body.Add(NativeTheme.Eyebrow("Tools"));
        _body.Add(NativeTheme.NavigationRow(
            "All actions",
            "Search every runner command",
            () => Navigation.PushAsync(new NativeCommandPage(Coordinator))));
    }
}
