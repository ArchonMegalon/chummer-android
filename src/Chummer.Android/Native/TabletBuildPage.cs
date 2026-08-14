using System.Globalization;
using Chummer.Contracts.Presentation;
using Chummer.Presentation.Overview;
using Chummer.Presentation.Rulesets;

namespace Chummer.Android.Native;

public sealed class TabletBuildPage : NativePageBase
{
    private readonly Grid _layout = new()
    {
        ColumnSpacing = 14,
        Padding = new Thickness(18, 16, 18, 24),
        AutomationId = "tablet-build-layout"
    };
    private readonly VerticalStackLayout _navigation = new() { Spacing = 12 };
    private readonly VerticalStackLayout _collection = new() { Spacing = 12 };
    private readonly VerticalStackLayout _inspector = new() { Spacing = 12 };
    private readonly ScrollView _navigationPane;
    private readonly ScrollView _collectionPane;
    private readonly ScrollView _inspectorPane;
    private readonly Dictionary<WorkspaceCollectionTextField, InputView> _textInputs = [];
    private readonly Dictionary<WorkspaceCollectionToggleField, Switch> _toggleInputs = [];
    private WorkspaceCollectionItemTarget? _selectedTarget;
    private string? _selectedAttributeName;
    private WorkspaceConditionMonitorTrack? _selectedConditionTrack;
    private Entry? _ratingInput;
    private Entry? _quantityInput;
    private Entry? _contactConnectionInput;
    private Entry? _contactLoyaltyInput;
    private Picker? _attributeBasePicker;
    private Picker? _attributeKarmaPicker;
    private Picker? _conditionFilledPicker;
    private Picker? _vehiclePhysicalDamagePicker;
    private Picker? _matrixDamagePicker;

    public TabletBuildPage(RunnerSessionCoordinator coordinator) : base(coordinator)
    {
        Title = "Build";
        AutomationId = "tablet-build-page";
        _navigationPane = CreatePane(_navigation, "tablet-build-navigation-pane");
        _collectionPane = CreatePane(_collection, "tablet-build-collection-pane");
        _inspectorPane = CreatePane(_inspector, "tablet-build-inspector-pane");
        _layout.Add(_navigationPane, 0, 0);
        _layout.Add(_collectionPane, 1, 0);
        _layout.Add(_inspectorPane, 2, 0);
        Content = _layout;
        SizeChanged += (_, _) => ApplyPaneWidths(Width);
        ToolbarItems.Add(new ToolbarItem
        {
            Text = "Save runner",
            Command = new Command(async () => await RunAsync(() => Coordinator.SaveAsync()))
        });
        ToolbarItems.Add(new ToolbarItem
        {
            Text = "Actions",
            Order = ToolbarItemOrder.Primary,
            Priority = 1,
            Command = new Command(async () => await Navigation.PushAsync(new NativeCommandPage(Coordinator)))
        });
    }

    protected override void Refresh()
    {
        WorkspaceCollectionEditorState? editor = Coordinator.State.ActiveCollectionEditor;
        ConditionMonitorEditorState? conditionMonitor = Coordinator.State.ActiveConditionMonitor;
        if (conditionMonitor is not null)
        {
            if (_selectedConditionTrack is null
                || !conditionMonitor.Tracks.Any(track => track.Track == _selectedConditionTrack))
            {
                _selectedConditionTrack = conditionMonitor.Tracks.FirstOrDefault()?.Track;
            }
        }
        else
        {
            _selectedConditionTrack = null;
        }

        bool attributeSection = AttributeWorkbenchProjector.IsAttributeSection(Coordinator.State.ActiveSectionId);
        if (attributeSection)
        {
            IReadOnlyList<AttributeWorkbenchRow> attributes = CurrentAttributes();
            if (string.IsNullOrWhiteSpace(_selectedAttributeName)
                || !attributes.Any(attribute => string.Equals(
                    attribute.AttributeName,
                    _selectedAttributeName,
                    StringComparison.OrdinalIgnoreCase)))
            {
                _selectedAttributeName = attributes.FirstOrDefault()?.AttributeName;
            }
        }
        else
        {
            _selectedAttributeName = null;
        }

        if (editor is null)
        {
            _selectedTarget = null;
        }
        else if (_selectedTarget is null
                 || !editor.Items.Any(item => CollectionItemEditorPage.TargetsMatch(item.Target, _selectedTarget)))
        {
            _selectedTarget = editor.Items.FirstOrDefault()?.Target;
        }

        BuildNavigationPane();
        BuildCollectionPane(editor);
        BuildInspectorPane(editor);
        ApplyPaneWidths(Width);
    }

    private void BuildNavigationPane()
    {
        _navigation.Clear();
        string runner = Coordinator.State.Profile?.Alias ?? Coordinator.State.Profile?.Name ?? "Runner";
        _navigation.Add(NativeTheme.Eyebrow(runner));
        _navigation.Add(NativeTheme.Title("Build areas", 23));
        _navigation.Add(NativeTheme.NavigationRow(
            "Origin dossier",
            "Identity, appearance and story",
            () => Navigation.PushAsync(new OriginDossierPage(Coordinator)),
            automationId: "tablet-origin-dossier"));
        foreach (NavigationTabDefinition tab in Coordinator.Surface.NavigationTabs)
        {
            bool active = string.Equals(tab.Id, Coordinator.Surface.ActiveTabId, StringComparison.Ordinal);
            _navigation.Add(NativeTheme.NavigationRow(
                RunnerSessionCoordinator.HumanizeId(tab.Id),
                active ? "Selected" : null,
                () => RunAsync(() => Coordinator.SelectTabAsync(tab.Id)),
                Coordinator.IsTabEnabled(tab),
                $"tablet-build-tab-{Token(tab.Id)}"));
        }

        if (Coordinator.Surface.WorkspaceActions.Count == 0)
        {
            return;
        }

        _navigation.Add(NativeTheme.Eyebrow("Section"));
        foreach (WorkspaceSurfaceActionDefinition action in Coordinator.Surface.WorkspaceActions)
        {
            bool active = string.Equals(action.Id, Coordinator.State.ActiveActionId, StringComparison.Ordinal);
            _navigation.Add(NativeTheme.NavigationRow(
                action.Label,
                active ? "Open" : null,
                () => RunAsync(() => Coordinator.ExecuteWorkspaceActionAsync(action)),
                automationId: $"tablet-build-action-{Token(action.Id)}"));
        }

        IReadOnlyList<SectionQuickActionDefinition> quickActions = SectionQuickActionCatalog.ForSection(
            Coordinator.Surface.ActiveRulesetId,
            Coordinator.State.ActiveSectionId);
        if (quickActions.Count > 0)
        {
            _navigation.Add(NativeTheme.Eyebrow("Edit"));
            foreach (SectionQuickActionDefinition action in quickActions)
            {
                _navigation.Add(NativeTheme.NavigationRow(
                    action.Label,
                    action.IsPrimary ? "Primary action" : null,
                    () => RunAsync(() => Coordinator.HandleUiControlAsync(action.ControlId)),
                    automationId: $"tablet-quick-{Token(action.ControlId)}"));
            }
        }
    }

    private void BuildCollectionPane(WorkspaceCollectionEditorState? editor)
    {
        _collection.Clear();
        _collection.Add(NativeTheme.Eyebrow("Collection"));
        _collection.Add(NativeTheme.Title(
            Coordinator.State.ActiveConditionMonitor is not null
                ? "Damage tracks"
                : AttributeWorkbenchProjector.IsAttributeSection(Coordinator.State.ActiveSectionId)
                ? "Attributes"
                : editor is null
                ? RunnerSessionCoordinator.HumanizeId(Coordinator.State.ActiveSectionId ?? "Details")
                : RunnerSessionCoordinator.HumanizeId(editor.NestedKind?.ToString() ?? editor.Kind.ToString()),
            23));

        if (Coordinator.State.ActiveConditionMonitor is { } conditionMonitor)
        {
            foreach (ConditionMonitorTrackState track in conditionMonitor.Tracks)
            {
                bool selected = _selectedConditionTrack == track.Track;
                Border row = NativeTheme.NavigationRow(
                    track.Label,
                    selected ? $"Selected · {track.Filled}/{track.EditableMaximum}" : $"{track.Filled}/{track.EditableMaximum} filled",
                    () =>
                    {
                        _selectedConditionTrack = track.Track;
                        BuildCollectionPane(editor);
                        BuildInspectorPane(editor);
                        return Task.CompletedTask;
                    },
                    automationId: $"tablet-condition-track-{ConditionMonitorEditPage.Token(track.Track)}");
                row.BackgroundColor = selected ? NativeTheme.SignalSoft : NativeTheme.Surface;
                _collection.Add(row);
            }
            return;
        }

        if (AttributeWorkbenchProjector.IsAttributeSection(Coordinator.State.ActiveSectionId))
        {
            IReadOnlyList<AttributeWorkbenchRow> attributes = CurrentAttributes();
            if (attributes.Count == 0)
            {
                _collection.Add(NativeTheme.Body("No attributes are available for this runner.", NativeTheme.Muted));
                return;
            }

            foreach (AttributeWorkbenchRow attribute in attributes)
            {
                bool selected = string.Equals(
                    attribute.AttributeName,
                    _selectedAttributeName,
                    StringComparison.OrdinalIgnoreCase);
                Border row = NativeTheme.NavigationRow(
                    attribute.DisplayName,
                    selected ? $"Selected · {attribute.TotalValue}" : attribute.TotalValue.ToString(CultureInfo.InvariantCulture),
                    () =>
                    {
                        _selectedAttributeName = attribute.AttributeName;
                        BuildCollectionPane(editor);
                        BuildInspectorPane(editor);
                        return Task.CompletedTask;
                    },
                    automationId: $"tablet-attribute-{Token(attribute.AttributeName)}");
                row.BackgroundColor = selected ? NativeTheme.SignalSoft : NativeTheme.Surface;
                _collection.Add(row);
            }
            return;
        }

        if (editor is null)
        {
            SectionRowState[] rows = Coordinator.State.ActiveSectionRows.Take(30).ToArray();
            if (rows.Length == 0)
            {
                _collection.Add(NativeTheme.Body("Choose a build area and section.", NativeTheme.Muted));
                return;
            }

            foreach (SectionRowState row in rows)
            {
                _collection.Add(NativeTheme.Card(NativeTheme.Metric(
                    BuildNavigation.RowLabel(row.Path),
                    row.Value)));
            }
            return;
        }

        if (editor.Items.Count == 0)
        {
            _collection.Add(NativeTheme.Body("No entries yet. Use a section action to add one.", NativeTheme.Muted));
            return;
        }

        foreach (WorkspaceCollectionItemEditorState item in editor.Items)
        {
            bool selected = _selectedTarget is not null
                && CollectionItemEditorPage.TargetsMatch(item.Target, _selectedTarget);
            (string title, string? metadata) = CollectionItemCopy(item.Label);
            string detail = metadata is null
                ? selected ? "Selected" : $"Entry {item.Index + 1}"
                : selected ? $"Selected · {metadata}" : metadata;
            Border row = NativeTheme.NavigationRow(
                title,
                detail,
                () =>
                {
                    _selectedTarget = item.Target;
                    BuildCollectionPane(editor);
                    BuildInspectorPane(editor);
                    return Task.CompletedTask;
                },
                automationId: $"tablet-collection-item-{Token(item.Target.NestedItemId ?? item.Target.ItemId)}");
            row.BackgroundColor = selected ? NativeTheme.SignalSoft : NativeTheme.Surface;
            _collection.Add(row);
        }
    }

    private void BuildInspectorPane(WorkspaceCollectionEditorState? editor)
    {
        _inspector.Clear();
        _textInputs.Clear();
        _toggleInputs.Clear();
        _ratingInput = null;
        _quantityInput = null;
        _contactConnectionInput = null;
        _contactLoyaltyInput = null;
        _attributeBasePicker = null;
        _attributeKarmaPicker = null;
        _conditionFilledPicker = null;
        _vehiclePhysicalDamagePicker = null;
        _matrixDamagePicker = null;
        _inspector.Add(NativeTheme.Eyebrow("Inspector"));

        if (Coordinator.State.ActiveConditionMonitor is { } conditionMonitor)
        {
            BuildConditionMonitorInspector(conditionMonitor);
            return;
        }

        if (AttributeWorkbenchProjector.IsAttributeSection(Coordinator.State.ActiveSectionId))
        {
            BuildAttributeInspector();
            return;
        }

        WorkspaceCollectionItemEditorState? item = editor?.Items.FirstOrDefault(candidate =>
            _selectedTarget is not null
            && CollectionItemEditorPage.TargetsMatch(candidate.Target, _selectedTarget));
        if (item is null)
        {
            _inspector.Add(NativeTheme.Title("Select an item", 23));
            _inspector.Add(NativeTheme.Body(
                "The selected item’s editable fields, limits, and state stay here.",
                NativeTheme.Muted));
            return;
        }

        _inspector.Add(NativeTheme.Title(item.Label, 23));
        foreach (WorkspaceCollectionTextValueState value in item.TextValues)
        {
            AddTextInput(value);
        }

        if (item.Rating is { } rating)
        {
            _ratingInput = AddNumericInput("Rating", rating.Value.ToString(CultureInfo.InvariantCulture), "tablet-rating");
        }

        if (item.Quantity is { } quantity)
        {
            _quantityInput = AddNumericInput("Quantity", quantity.Value.ToString(CultureInfo.InvariantCulture), "tablet-quantity");
        }

        if (item.Contact is { } contact)
        {
            AddContactRatings(contact);
        }

        if (item.PhysicalConditionMonitor is { } vehicleCondition)
        {
            AddVehiclePhysicalConditionMonitor(vehicleCondition);
        }
        if (item.MatrixConditionMonitor is { } vehicleMatrixCondition)
        {
            AddMatrixConditionMonitor(vehicleMatrixCondition, item.Target.Kind);
        }

        foreach (WorkspaceCollectionToggleValueState value in item.ToggleValues)
        {
            AddToggle(value);
        }

        Button save = NativeTheme.PrimaryButton("Apply changes");
        save.AutomationId = "tablet-inspector-save";
        save.Clicked += async (_, _) => await RunAsync(() => SaveInspectorAsync(item));
        _inspector.Add(save);
        AddLinkedCharacterInspector(item);
        AddInspectorActions(item, editor!.Items.Count);

        if (!string.IsNullOrWhiteSpace(Coordinator.State.Error))
        {
            _inspector.Add(NativeTheme.Body(Coordinator.State.Error!, NativeTheme.Danger));
        }
        else if (!string.IsNullOrWhiteSpace(Coordinator.Notice))
        {
            _inspector.Add(NativeTheme.Body(Coordinator.Notice!, NativeTheme.Muted));
        }
    }

    private void BuildConditionMonitorInspector(ConditionMonitorEditorState editor)
    {
        ConditionMonitorTrackState? track = editor.Tracks.FirstOrDefault(candidate => candidate.Track == _selectedConditionTrack);
        if (track is null)
        {
            _inspector.Add(NativeTheme.Title("Select a damage track", 23));
            return;
        }

        _inspector.Add(NativeTheme.Title(track.Label, 23));
        VerticalStackLayout summary = new() { Spacing = 9 };
        summary.Add(NativeTheme.Metric("Filled", $"{track.Filled} / {track.EditableMaximum}"));
        summary.Add(NativeTheme.Metric("Base track", track.TrackMaximum.ToString(CultureInfo.InvariantCulture)));
        if (track.Overflow > 0)
        {
            summary.Add(NativeTheme.Metric("Overflow", track.Overflow.ToString(CultureInfo.InvariantCulture)));
        }
        summary.Add(NativeTheme.Metric("Threshold offset", track.ThresholdOffset.ToString(CultureInfo.InvariantCulture)));
        _inspector.Add(NativeTheme.Card(summary));
        if (!editor.CareerEditable)
        {
            _inspector.Add(NativeTheme.Body(
                "Damage becomes editable after the runner enters career mode.",
                NativeTheme.Muted));
            return;
        }

        _inspector.Add(NativeTheme.FieldLabel("Filled boxes"));
        string token = ConditionMonitorEditPage.Token(track.Track);
        string[] values = Enumerable.Range(0, track.EditableMaximum + 1)
            .Select(value => value.ToString(CultureInfo.InvariantCulture))
            .ToArray();
        _conditionFilledPicker = new Picker
        {
            AutomationId = $"tablet-condition-filled-{token}",
            ItemsSource = values,
            SelectedIndex = Math.Clamp(track.Filled, 0, values.Length - 1),
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        _inspector.Add(_conditionFilledPicker);

        Button apply = NativeTheme.PrimaryButton("Apply damage track");
        apply.AutomationId = $"tablet-condition-save-{token}";
        apply.Clicked += async (_, _) => await RunAsync(() => Coordinator.ApplyConditionMonitorEditAsync(
            new ConditionMonitorEditRequest(track.Track, SelectedNumber(_conditionFilledPicker, track.Filled))));
        _inspector.Add(apply);

        Button clear = NativeTheme.SecondaryButton("Clear damage");
        clear.AutomationId = $"tablet-condition-clear-{token}";
        clear.IsEnabled = track.Filled > 0;
        clear.Clicked += async (_, _) => await RunAsync(() => Coordinator.ApplyConditionMonitorEditAsync(
            new ConditionMonitorEditRequest(track.Track, 0)));
        _inspector.Add(clear);
    }

    private void BuildAttributeInspector()
    {
        AttributeWorkbenchRow? attribute = CurrentAttributes().FirstOrDefault(candidate => string.Equals(
            candidate.AttributeName,
            _selectedAttributeName,
            StringComparison.OrdinalIgnoreCase));
        if (attribute is null)
        {
            _inspector.Add(NativeTheme.Title("Select an attribute", 23));
            return;
        }

        _inspector.Add(NativeTheme.Title(attribute.DisplayName, 23));
        VerticalStackLayout summary = new() { Spacing = 9 };
        summary.Add(NativeTheme.Metric("Current", attribute.TotalValue.ToString(CultureInfo.InvariantCulture)));
        summary.Add(NativeTheme.Metric("Natural range", $"{attribute.MetatypeMin}-{attribute.MetatypeMax}"));
        summary.Add(NativeTheme.Metric("Augmented max", attribute.MetatypeAugMax.ToString(CultureInfo.InvariantCulture)));
        _inspector.Add(NativeTheme.Card(summary));

        if (attribute.CareerMode)
        {
            _inspector.Add(NativeTheme.Body($"Available Karma: {attribute.AvailableKarma}", NativeTheme.Muted));
            Button improve = NativeTheme.PrimaryButton(
                attribute.UpgradeKarmaCost > 0 ? $"Improve · {attribute.UpgradeKarmaCost} Karma" : "At maximum");
            improve.AutomationId = $"tablet-attribute-improve-{Token(attribute.AttributeName)}";
            improve.IsEnabled = AttributeWorkbenchProjector.CanCareerAdvance(attribute);
            improve.Clicked += async (_, _) => await RunAsync(() => Coordinator.ApplyAttributeEditAsync(
                new AttributeEditRequest(attribute.AttributeName, "improve", attribute.TotalValue + 1)));
            _inspector.Add(improve);

            if (AttributeWorkbenchProjector.CanBurnEdge(attribute))
            {
                Button burn = NativeTheme.SecondaryButton("Burn Edge");
                burn.AutomationId = "tablet-attribute-burn-edge";
                burn.TextColor = NativeTheme.Danger;
                burn.Clicked += async (_, _) =>
                {
                    bool confirmed = await DisplayAlertAsync(
                        "Burn Edge?",
                        "This permanently reduces Edge by one.",
                        "Burn",
                        "Cancel");
                    if (confirmed)
                    {
                        await RunAsync(() => Coordinator.ApplyAttributeEditAsync(
                            new AttributeEditRequest(attribute.AttributeName, "burn", 0)));
                    }
                };
                _inspector.Add(burn);
            }
            return;
        }

        _inspector.Add(NativeTheme.FieldLabel("Base"));
        _attributeBasePicker = NumberPicker(
            $"tablet-attribute-base-{Token(attribute.AttributeName)}",
            attribute.EffectiveBaseMinimum,
            attribute.EffectiveBaseMaximum,
            attribute.BaseValue);
        _attributeBasePicker.IsEnabled = attribute.BaseUnlocked;
        _inspector.Add(_attributeBasePicker);
        _inspector.Add(NativeTheme.FieldLabel("Karma"));
        _attributeKarmaPicker = NumberPicker(
            $"tablet-attribute-karma-{Token(attribute.AttributeName)}",
            0,
            attribute.EffectiveKarmaMaximum,
            attribute.KarmaValue);
        _inspector.Add(_attributeKarmaPicker);

        Button save = NativeTheme.PrimaryButton("Apply attribute");
        save.AutomationId = $"tablet-attribute-save-{Token(attribute.AttributeName)}";
        save.Clicked += async (_, _) => await RunAsync(() => SaveAttributeAsync(attribute));
        _inspector.Add(save);
    }

    private async Task SaveAttributeAsync(AttributeWorkbenchRow attribute)
    {
        int baseValue = SelectedNumber(_attributeBasePicker, attribute.BaseValue);
        int karmaValue = SelectedNumber(_attributeKarmaPicker, attribute.KarmaValue);
        if (attribute.BaseUnlocked && baseValue != attribute.BaseValue)
        {
            await Coordinator.ApplyAttributeEditAsync(new AttributeEditRequest(attribute.AttributeName, "base", baseValue));
        }

        if (karmaValue != attribute.KarmaValue)
        {
            await Coordinator.ApplyAttributeEditAsync(new AttributeEditRequest(attribute.AttributeName, "karma", karmaValue));
        }

        if (baseValue == attribute.BaseValue && karmaValue == attribute.KarmaValue)
        {
            await DisplayAlertAsync("No changes", "Nothing has changed on this attribute.", "OK");
        }
    }

    private void AddTextInput(WorkspaceCollectionTextValueState value)
    {
        string label = RunnerSessionCoordinator.HumanizeId(value.Field.ToString());
        _inspector.Add(NativeTheme.FieldLabel(value.IsRequired ? $"{label} · required" : label));
        string automationId = $"tablet-field-{Token(value.Field.ToString())}";
        InputView input;
        if (value.Field == WorkspaceCollectionTextField.Notes)
        {
            Editor editor = NativeTheme.TextArea(automationId, value.Value);
            editor.MaxLength = value.MaximumLength;
            _inspector.Add(NativeTheme.Card(editor, new Thickness(12, 6)));
            input = editor;
        }
        else
        {
            Entry entry = NativeTheme.TextField(automationId, value.Value);
            entry.MaxLength = value.MaximumLength;
            _inspector.Add(entry);
            input = entry;
        }

        input.IsEnabled = value.IsEnabled;
        _textInputs.Add(value.Field, input);
    }

    private Entry AddNumericInput(string label, string value, string automationId)
    {
        _inspector.Add(NativeTheme.FieldLabel(label));
        Entry input = NativeTheme.TextField(automationId, value);
        input.Keyboard = Keyboard.Numeric;
        _inspector.Add(input);
        return input;
    }

    private void AddContactRatings(WorkspaceContactEditorState contact)
    {
        _inspector.Add(NativeTheme.Eyebrow("Contact ratings"));
        if (!contact.Exact)
        {
            _inspector.Add(NativeTheme.Body(
                "Connection and Loyalty are read-only because this runner's exact contact rules could not be resolved.",
                NativeTheme.Muted));
            return;
        }

        _contactConnectionInput = AddNumericInput(
            $"Connection · 1–{contact.ConnectionMaximum}",
            contact.Connection.ToString(CultureInfo.InvariantCulture),
            "tablet-contact-connection");
        _contactConnectionInput.IsEnabled = contact.ConnectionEditable;
        _contactLoyaltyInput = AddNumericInput(
            $"Loyalty · 1–{contact.LoyaltyMaximum}",
            contact.Loyalty.ToString(CultureInfo.InvariantCulture),
            "tablet-contact-loyalty");
        _contactLoyaltyInput.IsEnabled = contact.LoyaltyEditable;
    }

    private void AddToggle(WorkspaceCollectionToggleValueState value)
    {
        Switch toggle = new()
        {
            AutomationId = $"tablet-toggle-{Token(value.Field.ToString())}",
            IsToggled = value.Value,
            IsEnabled = value.IsEnabled,
            OnColor = NativeTheme.Signal
        };
        Grid row = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        row.Add(NativeTheme.FieldLabel(RunnerSessionCoordinator.HumanizeId(value.Field.ToString())), 0, 0);
        row.Add(toggle, 1, 0);
        _toggleInputs.Add(value.Field, toggle);
        _inspector.Add(NativeTheme.Card(row));
    }

    private void AddVehiclePhysicalConditionMonitor(WorkspaceItemConditionMonitorState condition)
    {
        _inspector.Add(NativeTheme.Eyebrow("Vehicle condition"));
        VerticalStackLayout summary = new() { Spacing = 8 };
        summary.Add(NativeTheme.Metric(
            condition.Label,
            condition.MaximumExact
                ? $"{condition.Filled} / {condition.Maximum}"
                : $"{condition.Filled} / unavailable"));
        _inspector.Add(NativeTheme.Card(summary));

        if (!condition.MaximumExact)
        {
            _inspector.Add(NativeTheme.Body(
                "Damage is read-only because this saved runner does not contain enough vehicle-mod data to derive the exact track maximum.",
                NativeTheme.Muted));
            return;
        }
        if (!condition.Editable)
        {
            _inspector.Add(NativeTheme.Body(
                "Vehicle damage becomes editable after the runner enters career mode.",
                NativeTheme.Muted));
            return;
        }

        _inspector.Add(NativeTheme.FieldLabel("Filled physical boxes"));
        _vehiclePhysicalDamagePicker = new Picker
        {
            AutomationId = "tablet-vehicle-physical-damage",
            ItemsSource = Enumerable.Range(0, condition.Maximum + 1)
                .Select(value => value.ToString(CultureInfo.InvariantCulture))
                .ToArray(),
            SelectedIndex = Math.Clamp(condition.Filled, 0, condition.Maximum),
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        _inspector.Add(_vehiclePhysicalDamagePicker);
    }

    private void AddMatrixConditionMonitor(
        WorkspaceItemConditionMonitorState condition,
        WorkspaceCollectionKind kind)
    {
        _inspector.Add(NativeTheme.Eyebrow("Matrix condition"));
        VerticalStackLayout summary = new() { Spacing = 8 };
        summary.Add(NativeTheme.Metric(
            condition.Label,
            condition.MaximumExact
                ? $"{condition.Filled} / {condition.Maximum}"
                : $"{condition.Filled} / unavailable"));
        _inspector.Add(NativeTheme.Card(summary));

        if (!condition.MaximumExact)
        {
            _inspector.Add(NativeTheme.Body(
                "Matrix damage is read-only because this saved runner does not contain enough device, mod, or child-gear data to derive the exact track maximum.",
                NativeTheme.Muted));
            return;
        }
        if (!condition.Editable)
        {
            _inspector.Add(NativeTheme.Body(
                $"{RunnerSessionCoordinator.HumanizeId(kind.ToString())} Matrix damage becomes editable after the runner enters career mode.",
                NativeTheme.Muted));
            return;
        }

        _inspector.Add(NativeTheme.FieldLabel("Filled Matrix boxes"));
        _matrixDamagePicker = new Picker
        {
            AutomationId = kind switch
            {
                WorkspaceCollectionKind.Vehicle => "tablet-vehicle-matrix-damage",
                WorkspaceCollectionKind.Gear => "tablet-gear-matrix-damage",
                WorkspaceCollectionKind.Armor => "tablet-armor-matrix-damage",
                WorkspaceCollectionKind.Weapon => "tablet-weapon-matrix-damage",
                WorkspaceCollectionKind.Cyberware => "tablet-cyberware-matrix-damage",
                _ => throw new InvalidOperationException($"Unsupported Matrix condition-monitor kind '{kind}'.")
            },
            ItemsSource = Enumerable.Range(0, condition.Maximum + 1)
                .Select(value => value.ToString(CultureInfo.InvariantCulture))
                .ToArray(),
            SelectedIndex = Math.Clamp(condition.Filled, 0, condition.Maximum),
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        _inspector.Add(_matrixDamagePicker);
    }

    private async Task SaveInspectorAsync(WorkspaceCollectionItemEditorState item)
    {
        Dictionary<WorkspaceCollectionTextField, string?> textChanges = [];
        foreach (WorkspaceCollectionTextValueState original in item.TextValues)
        {
            if (!original.IsEnabled)
            {
                continue;
            }

            string value = _textInputs[original.Field].Text ?? string.Empty;
            if (original.IsRequired && string.IsNullOrWhiteSpace(value))
            {
                await DisplayAlertAsync("Name required", "Enter a name before saving.", "OK");
                return;
            }

            if (!string.Equals(value, original.Value, StringComparison.Ordinal))
            {
                textChanges[original.Field] = value;
            }
        }

        int? ratingChange = null;
        if (item.Rating is { } rating)
        {
            if (!int.TryParse(_ratingInput?.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                || value < rating.Minimum
                || value > rating.Maximum)
            {
                await DisplayAlertAsync("Invalid rating", $"Enter a whole number from {rating.Minimum} to {rating.Maximum}.", "OK");
                return;
            }
            ratingChange = value != rating.Value ? value : null;
        }

        decimal? quantityChange = null;
        if (item.Quantity is { } quantity)
        {
            if (!decimal.TryParse(_quantityInput?.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value)
                || value <= quantity.MinimumExclusive
                || value > quantity.Maximum)
            {
                await DisplayAlertAsync(
                    "Invalid quantity",
                    $"Enter a value greater than {quantity.MinimumExclusive} and no greater than {quantity.Maximum}.",
                    "OK");
                return;
            }
            quantityChange = value != quantity.Value ? value : null;
        }

        int? contactConnectionChange = null;
        int? contactLoyaltyChange = null;
        if (item.Contact is { Exact: true } contact)
        {
            if (!int.TryParse(
                    _contactConnectionInput?.Text,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int connection)
                || connection < 1
                || connection > contact.ConnectionMaximum)
            {
                await DisplayAlertAsync(
                    "Invalid Connection",
                    $"Enter a whole number from 1 to {contact.ConnectionMaximum}.",
                    "OK");
                return;
            }
            if (!int.TryParse(
                    _contactLoyaltyInput?.Text,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int loyalty)
                || loyalty < 1
                || loyalty > contact.LoyaltyMaximum)
            {
                await DisplayAlertAsync(
                    "Invalid Loyalty",
                    $"Enter a whole number from 1 to {contact.LoyaltyMaximum}.",
                    "OK");
                return;
            }

            contactConnectionChange = contact.ConnectionEditable && connection != contact.Connection
                ? connection
                : null;
            contactLoyaltyChange = contact.LoyaltyEditable && loyalty != contact.Loyalty
                ? loyalty
                : null;
        }

        Dictionary<WorkspaceCollectionToggleField, bool> toggleChanges = [];
        foreach (WorkspaceCollectionToggleValueState original in item.ToggleValues)
        {
            bool value = _toggleInputs[original.Field].IsToggled;
            if (original.IsEnabled && value != original.Value)
            {
                toggleChanges[original.Field] = value;
            }
        }

        int? vehiclePhysicalDamageChange = null;
        if (item.PhysicalConditionMonitor is { Editable: true, MaximumExact: true } vehicleCondition)
        {
            int value = _vehiclePhysicalDamagePicker?.SelectedIndex ?? -1;
            if (value < 0 || value > vehicleCondition.Maximum)
            {
                await DisplayAlertAsync(
                    "Invalid damage",
                    $"Choose a value from 0 to {vehicleCondition.Maximum}.",
                    "OK");
                return;
            }
            vehiclePhysicalDamageChange = value != vehicleCondition.Filled ? value : null;
        }

        int? vehicleMatrixDamageChange = null;
        int? gearMatrixDamageChange = null;
        int? armorMatrixDamageChange = null;
        int? weaponMatrixDamageChange = null;
        int? cyberwareMatrixDamageChange = null;
        if (item.MatrixConditionMonitor is { Editable: true, MaximumExact: true } vehicleMatrixCondition)
        {
            int value = _matrixDamagePicker?.SelectedIndex ?? -1;
            if (value < 0 || value > vehicleMatrixCondition.Maximum)
            {
                await DisplayAlertAsync(
                    "Invalid Matrix damage",
                    $"Choose a value from 0 to {vehicleMatrixCondition.Maximum}.",
                    "OK");
                return;
            }
            if (value != vehicleMatrixCondition.Filled)
            {
                switch (item.Target.Kind)
                {
                    case WorkspaceCollectionKind.Vehicle:
                        vehicleMatrixDamageChange = value;
                        break;
                    case WorkspaceCollectionKind.Gear:
                        gearMatrixDamageChange = value;
                        break;
                    case WorkspaceCollectionKind.Armor:
                        armorMatrixDamageChange = value;
                        break;
                    case WorkspaceCollectionKind.Weapon:
                        weaponMatrixDamageChange = value;
                        break;
                    case WorkspaceCollectionKind.Cyberware:
                        cyberwareMatrixDamageChange = value;
                        break;
                    default:
                        await DisplayAlertAsync(
                            "Unsupported Matrix item",
                            "Reload the section before editing Matrix damage.",
                            "OK");
                        return;
                }
            }
        }

        if (textChanges.Count == 0
            && ratingChange is null
            && quantityChange is null
            && contactConnectionChange is null
            && contactLoyaltyChange is null
            && toggleChanges.Count == 0
            && vehiclePhysicalDamageChange is null
            && vehicleMatrixDamageChange is null
            && gearMatrixDamageChange is null
            && armorMatrixDamageChange is null
            && weaponMatrixDamageChange is null
            && cyberwareMatrixDamageChange is null)
        {
            await DisplayAlertAsync("No changes", "Nothing has changed on this item.", "OK");
            return;
        }

        await Coordinator.ApplyCollectionMutationAsync(new WorkspacePatchCollectionItemRequest(
            item.Target,
            TextValues: textChanges,
            Rating: ratingChange,
            Quantity: quantityChange,
            ToggleValues: toggleChanges,
            VehiclePhysicalDamage: vehiclePhysicalDamageChange,
            VehicleMatrixDamage: vehicleMatrixDamageChange,
            GearMatrixDamage: gearMatrixDamageChange,
            ArmorMatrixDamage: armorMatrixDamageChange,
            WeaponMatrixDamage: weaponMatrixDamageChange,
            CyberwareMatrixDamage: cyberwareMatrixDamageChange,
            ContactConnection: contactConnectionChange,
            ContactLoyalty: contactLoyaltyChange));
    }

    private void AddInspectorActions(WorkspaceCollectionItemEditorState item, int itemCount)
    {
        _inspector.Add(NativeTheme.Eyebrow("Order and children"));
        HorizontalStackLayout order = new() { Spacing = 10 };
        Button up = NativeTheme.SecondaryButton("Move up");
        up.AutomationId = "tablet-inspector-move-up";
        up.IsEnabled = item.CanMove && item.Index > 0;
        up.Clicked += async (_, _) => await RunAsync(() => Coordinator.ApplyCollectionMutationAsync(
            new WorkspaceMoveCollectionItemRequest(item.Target, item.Index - 1)));
        order.Add(up);
        Button down = NativeTheme.SecondaryButton("Move down");
        down.AutomationId = "tablet-inspector-move-down";
        down.IsEnabled = item.CanMove && item.Index + 1 < itemCount;
        down.Clicked += async (_, _) => await RunAsync(() => Coordinator.ApplyCollectionMutationAsync(
            new WorkspaceMoveCollectionItemRequest(item.Target, item.Index + 1)));
        order.Add(down);
        _inspector.Add(order);

        foreach (WorkspaceNestedCollectionKind nestedKind in item.AddableNestedKinds)
        {
            _inspector.Add(NativeTheme.NavigationRow(
                $"Add {RunnerSessionCoordinator.HumanizeId(nestedKind.ToString())}",
                "Keep the parent and collection visible",
                () => Navigation.PushAsync(new NestedCollectionAddPage(Coordinator, item.Target, nestedKind)),
                automationId: $"tablet-inspector-add-{Token(nestedKind.ToString())}"));
        }

        Button delete = NativeTheme.SecondaryButton("Delete item");
        delete.AutomationId = "tablet-inspector-delete";
        delete.TextColor = NativeTheme.Danger;
        delete.IsEnabled = item.CanDelete;
        delete.Clicked += async (_, _) =>
        {
            bool confirmed = await DisplayAlertAsync("Delete item?", $"Delete {item.Label}?", "Delete", "Cancel");
            if (confirmed)
            {
                await RunAsync(() => Coordinator.ApplyCollectionMutationAsync(
                    new WorkspaceDeleteCollectionItemRequest(item.Target)));
            }
        };
        _inspector.Add(delete);
    }

    private void AddLinkedCharacterInspector(WorkspaceCollectionItemEditorState item)
    {
        if (item.LinkedCharacter is not { } linked)
        {
            return;
        }

        _inspector.Add(NativeTheme.Eyebrow("Linked runner"));
        Label status = NativeTheme.Body(
            linked.IsLinked
                ? linked.IdentityResolved
                    ? $"{item.Label} · {linked.DisplayName}"
                    : $"Unavailable · {linked.DisplayName}"
                : "No linked Chummer5 runner",
            linked.IdentityResolved || !linked.IsLinked ? NativeTheme.Muted : NativeTheme.Danger);
        status.AutomationId = "tablet-linked-status";
        _inspector.Add(NativeTheme.Card(status));

        HorizontalStackLayout actions = new() { Spacing = 10 };
        Button attach = NativeTheme.SecondaryButton(linked.IsLinked ? "Replace" : "Attach");
        attach.AutomationId = "tablet-linked-attach";
        attach.IsEnabled = linked.CanAttach;
        attach.Clicked += async (_, _) => await RunAsync(() => Coordinator.AttachLinkedCharacterAsync(item.Target));
        actions.Add(attach);

        Button remove = NativeTheme.SecondaryButton("Remove");
        remove.AutomationId = "tablet-linked-remove";
        remove.IsEnabled = linked.CanRemove;
        remove.TextColor = NativeTheme.Danger;
        remove.Clicked += async (_, _) =>
        {
            bool confirmed = await DisplayAlertAsync(
                "Remove linked runner?",
                "The original saved contact or pet identity will be shown again.",
                "Remove link",
                "Cancel");
            if (confirmed)
            {
                await RunAsync(() => Coordinator.RemoveLinkedCharacterAsync(item.Target));
            }
        };
        actions.Add(remove);
        _inspector.Add(actions);
    }

    private void ApplyPaneWidths(double width)
    {
        double effectiveWidth = width > 0 ? width : TabletLayoutPolicy.ExpandedWidthDip;
        bool wide = TabletLayoutPolicy.UseWideInspector(effectiveWidth);
        _layout.ColumnDefinitions.Clear();
        _layout.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(wide ? 260 : 220)));
        _layout.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(wide ? 360 : 300)));
        _layout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
    }

    private static ScrollView CreatePane(View content, string automationId)
        => new()
        {
            AutomationId = automationId,
            Content = new Border
            {
                BackgroundColor = NativeTheme.Paper,
                Padding = new Thickness(8),
                Content = content
            }
        };

    private IReadOnlyList<AttributeWorkbenchRow> CurrentAttributes()
        => AttributeWorkbenchProjector.BuildRows(
            Coordinator.State.ActiveSectionId,
            Coordinator.State.ActiveSectionJson ?? string.Empty);

    private static Picker NumberPicker(string automationId, int minimum, int maximum, int selected)
    {
        int safeMaximum = Math.Max(minimum, maximum);
        string[] values = Enumerable.Range(minimum, safeMaximum - minimum + 1)
            .Select(value => value.ToString(CultureInfo.InvariantCulture))
            .ToArray();
        return new Picker
        {
            AutomationId = automationId,
            ItemsSource = values,
            SelectedIndex = Math.Clamp(selected - minimum, 0, values.Length - 1),
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
    }

    private static int SelectedNumber(Picker? picker, int fallback)
        => picker?.SelectedItem is string selected
            && int.TryParse(selected, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                ? value
                : fallback;

    private static string Token(string value) => CollectionItemEditorPage.Token(value);

    private static (string Title, string? Metadata) CollectionItemCopy(string label)
    {
        int separator = label.IndexOf(" · ", StringComparison.Ordinal);
        return separator > 0
            ? (label[..separator], label[(separator + 3)..])
            : (label, null);
    }
}
