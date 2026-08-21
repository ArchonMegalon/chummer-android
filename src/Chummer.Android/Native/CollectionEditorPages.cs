using System.Globalization;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class CollectionItemEditorPage : NativePageBase
{
    private readonly WorkspaceCollectionItemTarget _target;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };
    private readonly Dictionary<WorkspaceCollectionTextField, InputView> _textInputs = [];
    private readonly Dictionary<WorkspaceCollectionToggleField, Switch> _toggleInputs = [];
    private readonly Dictionary<WorkspaceCollectionIntegerField, Entry> _integerInputs = [];
    private Entry? _ratingInput;
    private Entry? _quantityInput;
    private Entry? _contactConnectionInput;
    private Entry? _contactLoyaltyInput;
    private Picker? _vehiclePhysicalDamagePicker;
    private Picker? _matrixDamagePicker;

    public CollectionItemEditorPage(
        RunnerSessionCoordinator coordinator,
        WorkspaceCollectionItemTarget target) : base(coordinator)
    {
        _target = target;
        Title = RunnerSessionCoordinator.HumanizeId(target.NestedKind?.ToString() ?? target.Kind.ToString());
        AutomationId = $"collection-editor-{Token(target.Kind.ToString())}-{Token(target.NestedItemId ?? target.ItemId)}";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        WorkspaceCollectionItemEditorState? item = FindCurrentItem();
        _body.Clear();
        _textInputs.Clear();
        _toggleInputs.Clear();
        _integerInputs.Clear();
        _ratingInput = null;
        _quantityInput = null;
        _contactConnectionInput = null;
        _contactLoyaltyInput = null;
        _vehiclePhysicalDamagePicker = null;
        _matrixDamagePicker = null;

        _body.Add(NativeTheme.Eyebrow(_target.NestedKind?.ToString() ?? _target.Kind.ToString()));
        if (item is null)
        {
            _body.Add(NativeTheme.Title("Item unavailable"));
            _body.Add(NativeTheme.Body(
                "This item no longer has a unique stable identity. Reload the section before editing.",
                NativeTheme.Danger));
            return;
        }

        _body.Add(NativeTheme.Title(item.Label));
        foreach (WorkspaceCollectionTextValueState value in item.TextValues)
        {
            AddTextField(value);
        }

        if (item.Rating is { } rating)
        {
            _ratingInput = AddNumberField(
                "Rating",
                rating.Value.ToString(CultureInfo.InvariantCulture),
                $"collection-rating-{TargetToken()}");
        }

        if (item.Quantity is { } quantity)
        {
            _quantityInput = AddNumberField(
                "Quantity",
                quantity.Value.ToString(CultureInfo.InvariantCulture),
                $"collection-quantity-{TargetToken()}");
        }

        foreach (WorkspaceCollectionIntegerValueState value in item.IntegerValues)
        {
            AddIntegerField(value);
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

        Button save = NativeTheme.PrimaryButton("Save changes");
        save.AutomationId = $"collection-save-{TargetToken()}";
        save.Clicked += async (_, _) => await RunWithConditionalRefreshAsync(() => SaveAsync(item));
        _body.Add(save);

        AddLinkedCharacterActions(item);
        AddMoveAndDeleteActions(item);
        AddNestedActions(item);
        AddVehicleLocationActions(item);

        if (!string.IsNullOrWhiteSpace(Coordinator.State.Error))
        {
            _body.Add(NativeTheme.Body(Coordinator.State.Error!, NativeTheme.Danger));
        }
        else if (!string.IsNullOrWhiteSpace(Coordinator.Notice))
        {
            _body.Add(NativeTheme.Body(Coordinator.Notice!, NativeTheme.Muted));
        }
    }

    private WorkspaceCollectionItemEditorState? FindCurrentItem()
        => Coordinator.State.ActiveCollectionEditor?.Items.FirstOrDefault(item => TargetsMatch(item.Target, _target));

    private void AddTextField(WorkspaceCollectionTextValueState value)
    {
        string label = RunnerSessionCoordinator.HumanizeId(value.Field.ToString());
        VerticalStackLayout field = new() { Spacing = 5 };
        field.Add(NativeTheme.FieldLabel(value.IsRequired ? $"{label} · required" : label));
        string automationId = $"collection-field-{Token(value.Field.ToString())}-{TargetToken()}";
        InputView input;
        if (value.Field == WorkspaceCollectionTextField.Notes)
        {
            Editor editor = NativeTheme.TextArea(automationId, value.Value);
            editor.MaxLength = value.MaximumLength;
            field.Add(NativeTheme.Card(editor, new Thickness(12, 6)));
            input = editor;
        }
        else
        {
            Entry entry = NativeTheme.TextField(automationId, value.Value);
            entry.MaxLength = value.MaximumLength;
            entry.IsEnabled = value.IsEnabled;
            field.Add(entry);
            input = entry;
        }

        input.IsEnabled = value.IsEnabled;

        _textInputs.Add(value.Field, input);
        _body.Add(field);
    }

    private Entry AddNumberField(string label, string value, string automationId)
    {
        VerticalStackLayout field = new() { Spacing = 5 };
        field.Add(NativeTheme.FieldLabel(label));
        Entry input = NativeTheme.TextField(automationId, value);
        input.Keyboard = Keyboard.Numeric;
        field.Add(input);
        _body.Add(field);
        return input;
    }

    private void AddIntegerField(WorkspaceCollectionIntegerValueState value)
    {
        string label = value.Label ?? value.Field switch
        {
            WorkspaceCollectionIntegerField.Services => "Services / tasks owed",
            WorkspaceCollectionIntegerField.Force => "Force / Rating",
            _ => RunnerSessionCoordinator.HumanizeId(value.Field.ToString())
        };
        Entry input = AddNumberField(
            label,
            value.Value.ToString(CultureInfo.InvariantCulture),
            $"collection-integer-{Token(value.Field.ToString())}-{TargetToken()}");
        input.IsEnabled = value.IsEnabled;
        _integerInputs.Add(value.Field, input);
    }

    private void AddContactRatings(WorkspaceContactEditorState contact)
    {
        _body.Add(NativeTheme.Eyebrow("Contact ratings"));
        if (!contact.Exact)
        {
            _body.Add(NativeTheme.Body(
                "Connection and Loyalty are read-only because this runner's exact contact rules could not be resolved.",
                NativeTheme.Muted));
            return;
        }

        _contactConnectionInput = AddNumberField(
            $"Connection · 1–{contact.ConnectionMaximum}",
            contact.Connection.ToString(CultureInfo.InvariantCulture),
            $"collection-contact-connection-{TargetToken()}");
        _contactConnectionInput.IsEnabled = contact.ConnectionEditable;
        _contactLoyaltyInput = AddNumberField(
            $"Loyalty · 1–{contact.LoyaltyMaximum}",
            contact.Loyalty.ToString(CultureInfo.InvariantCulture),
            $"collection-contact-loyalty-{TargetToken()}");
        _contactLoyaltyInput.IsEnabled = contact.LoyaltyEditable;
    }

    private void AddToggle(WorkspaceCollectionToggleValueState value)
    {
        Switch toggle = new()
        {
            IsToggled = value.Value,
            IsEnabled = value.IsEnabled,
            AutomationId = $"collection-toggle-{Token(value.Field.ToString())}-{TargetToken()}",
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
        _body.Add(NativeTheme.Card(row));
    }

    private void AddVehiclePhysicalConditionMonitor(WorkspaceItemConditionMonitorState condition)
    {
        _body.Add(NativeTheme.Eyebrow("Condition monitor"));
        VerticalStackLayout summary = new() { Spacing = 8 };
        summary.Add(NativeTheme.Metric(
            condition.Label,
            condition.MaximumExact
                ? $"{condition.Filled} / {condition.Maximum}"
                : $"{condition.Filled} / unavailable"));
        _body.Add(NativeTheme.Card(summary));

        if (!condition.MaximumExact)
        {
            _body.Add(NativeTheme.Body(
                "Damage is read-only because this saved runner does not contain enough vehicle-mod data to derive the exact track maximum.",
                NativeTheme.Muted));
            return;
        }
        if (!condition.Editable)
        {
            _body.Add(NativeTheme.Body(
                "Vehicle damage becomes editable after the runner enters career mode.",
                NativeTheme.Muted));
            return;
        }

        _body.Add(NativeTheme.FieldLabel("Filled physical boxes"));
        _vehiclePhysicalDamagePicker = new Picker
        {
            AutomationId = $"collection-vehicle-physical-damage-{TargetToken()}",
            ItemsSource = Enumerable.Range(0, condition.Maximum + 1)
                .Select(value => value.ToString(CultureInfo.InvariantCulture))
                .ToArray(),
            SelectedIndex = Math.Clamp(condition.Filled, 0, condition.Maximum),
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        _body.Add(_vehiclePhysicalDamagePicker);
    }

    private void AddMatrixConditionMonitor(
        WorkspaceItemConditionMonitorState condition,
        WorkspaceCollectionKind kind)
    {
        _body.Add(NativeTheme.Eyebrow("Matrix condition"));
        VerticalStackLayout summary = new() { Spacing = 8 };
        summary.Add(NativeTheme.Metric(
            condition.Label,
            condition.MaximumExact
                ? $"{condition.Filled} / {condition.Maximum}"
                : $"{condition.Filled} / unavailable"));
        _body.Add(NativeTheme.Card(summary));

        if (!condition.MaximumExact)
        {
            _body.Add(NativeTheme.Body(
                "Matrix damage is read-only because this saved runner does not contain enough device, mod, or child-gear data to derive the exact track maximum.",
                NativeTheme.Muted));
            return;
        }
        if (!condition.Editable)
        {
            _body.Add(NativeTheme.Body(
                $"{RunnerSessionCoordinator.HumanizeId(kind.ToString())} Matrix damage becomes editable after the runner enters career mode.",
                NativeTheme.Muted));
            return;
        }

        _body.Add(NativeTheme.FieldLabel("Filled Matrix boxes"));
        _matrixDamagePicker = new Picker
        {
            AutomationId = kind switch
            {
                WorkspaceCollectionKind.Vehicle => $"collection-vehicle-matrix-damage-{TargetToken()}",
                WorkspaceCollectionKind.Gear => $"collection-gear-matrix-damage-{TargetToken()}",
                WorkspaceCollectionKind.Armor => $"collection-armor-matrix-damage-{TargetToken()}",
                WorkspaceCollectionKind.Weapon => $"collection-weapon-matrix-damage-{TargetToken()}",
                WorkspaceCollectionKind.Cyberware => $"collection-cyberware-matrix-damage-{TargetToken()}",
                _ => throw new InvalidOperationException($"Unsupported Matrix condition-monitor kind '{kind}'.")
            },
            ItemsSource = Enumerable.Range(0, condition.Maximum + 1)
                .Select(value => value.ToString(CultureInfo.InvariantCulture))
                .ToArray(),
            SelectedIndex = Math.Clamp(condition.Filled, 0, condition.Maximum),
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        _body.Add(_matrixDamagePicker);
    }

    private async Task<bool> SaveAsync(WorkspaceCollectionItemEditorState item)
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
                return false;
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
                await DisplayAlertAsync(
                    "Invalid rating",
                    $"Enter a whole number from {rating.Minimum} to {rating.Maximum}.",
                    "OK");
                return false;
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
                return false;
            }

            quantityChange = value != quantity.Value ? value : null;
        }

        Dictionary<WorkspaceCollectionIntegerField, int> integerChanges = [];
        foreach (WorkspaceCollectionIntegerValueState original in item.IntegerValues)
        {
            if (!original.IsEnabled)
            {
                continue;
            }

            if (!int.TryParse(
                    _integerInputs[original.Field].Text,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int value)
                || value < original.Minimum
                || value > original.Maximum)
            {
                await DisplayAlertAsync(
                    "Invalid value",
                    $"Enter a whole number from {original.Minimum} to {original.Maximum}.",
                    "OK");
                return false;
            }

            if (value != original.Value)
            {
                integerChanges[original.Field] = value;
            }
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
                return false;
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
                return false;
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
                return false;
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
                return false;
            }
            if (value != vehicleMatrixCondition.Filled)
            {
                switch (_target.Kind)
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
                        return false;
                }
            }
        }

        if (textChanges.Count == 0
            && ratingChange is null
            && quantityChange is null
            && integerChanges.Count == 0
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
            return false;
        }

        await Coordinator.ApplyCollectionMutationAsync(new WorkspacePatchCollectionItemRequest(
            _target,
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
            ContactLoyalty: contactLoyaltyChange,
            IntegerValues: integerChanges));
        return true;
    }

    private void AddMoveAndDeleteActions(WorkspaceCollectionItemEditorState item)
    {
        _body.Add(NativeTheme.Eyebrow("Order and removal"));
        HorizontalStackLayout order = new() { Spacing = 10 };
        Button up = NativeTheme.SecondaryButton("Move up");
        up.AutomationId = $"collection-move-up-{TargetToken()}";
        up.IsEnabled = item.CanMove && item.Index > 0;
        up.Clicked += async (_, _) => await RunAsync(() => Coordinator.ApplyCollectionMutationAsync(
            new WorkspaceMoveCollectionItemRequest(_target, item.Index - 1)));
        order.Add(up);

        Button down = NativeTheme.SecondaryButton("Move down");
        down.AutomationId = $"collection-move-down-{TargetToken()}";
        int itemCount = Coordinator.State.ActiveCollectionEditor?.Items.Count ?? 0;
        down.IsEnabled = item.CanMove && item.Index + 1 < itemCount;
        down.Clicked += async (_, _) => await RunAsync(() => Coordinator.ApplyCollectionMutationAsync(
            new WorkspaceMoveCollectionItemRequest(_target, item.Index + 1)));
        order.Add(down);
        _body.Add(order);

        Button delete = NativeTheme.SecondaryButton("Delete item");
        delete.AutomationId = $"collection-delete-{TargetToken()}";
        delete.TextColor = NativeTheme.Danger;
        delete.IsEnabled = item.CanDelete;
        delete.Clicked += async (_, _) =>
        {
            bool confirmed = await DisplayAlertAsync(
                "Delete item?",
                $"Delete {item.Label}? This change is saved to the open runner immediately.",
                "Delete",
                "Cancel");
            if (!confirmed)
            {
                return;
            }

            await RunAsync(() => Coordinator.ApplyCollectionMutationAsync(
                new WorkspaceDeleteCollectionItemRequest(_target)));
            if (Coordinator.State.Error is null)
            {
                await Navigation.PopAsync();
            }
        };
        _body.Add(delete);
    }

    private void AddLinkedCharacterActions(WorkspaceCollectionItemEditorState item)
    {
        if (item.LinkedCharacter is not { } linked)
        {
            return;
        }

        _body.Add(NativeTheme.Eyebrow("Linked runner"));
        Label status = NativeTheme.Body(
            linked.IsLinked
                ? linked.IdentityResolved
                    ? $"Linked to {item.Label} via {linked.DisplayName}."
                    : $"Linked file {linked.DisplayName} is unavailable. Replace it or remove the link."
                : "No Chummer5 runner is linked to this item.",
            linked.IdentityResolved || !linked.IsLinked ? NativeTheme.Muted : NativeTheme.Danger);
        status.AutomationId = $"collection-linked-status-{TargetToken()}";
        _body.Add(NativeTheme.Card(status));

        Button attach = NativeTheme.SecondaryButton(linked.IsLinked ? "Replace linked runner" : "Attach linked runner");
        attach.AutomationId = $"collection-linked-attach-{TargetToken()}";
        attach.IsEnabled = linked.CanAttach;
        attach.Clicked += async (_, _) => await RunAsync(() => Coordinator.AttachLinkedCharacterAsync(_target));
        _body.Add(attach);

        Button remove = NativeTheme.SecondaryButton("Remove linked runner");
        remove.AutomationId = $"collection-linked-remove-{TargetToken()}";
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
                await RunAsync(() => Coordinator.RemoveLinkedCharacterAsync(_target));
            }
        };
        _body.Add(remove);
    }

    private void AddNestedActions(WorkspaceCollectionItemEditorState item)
    {
        if (item.AddableNestedKinds.Count == 0)
        {
            return;
        }

        _body.Add(NativeTheme.Eyebrow("Add child"));
        foreach (WorkspaceNestedCollectionKind nestedKind in item.AddableNestedKinds)
        {
            _body.Add(NativeTheme.NavigationRow(
                $"Add {RunnerSessionCoordinator.HumanizeId(nestedKind.ToString())}",
                "Create a child item under this entry",
                () => Navigation.PushAsync(new NestedCollectionAddPage(Coordinator, _target, nestedKind)),
                automationId: $"collection-add-{Token(nestedKind.ToString())}-{TargetToken()}"));
        }
    }

    private void AddVehicleLocationActions(WorkspaceCollectionItemEditorState item)
    {
        if (_target.Kind != WorkspaceCollectionKind.Vehicle
            || _target.NestedKind is not null
            || item.VehicleLocations is null
            || Coordinator.State.WorkspaceId is not { } workspaceId
            || !Guid.TryParseExact(_target.ItemId, "D", out Guid vehicleId)
            || vehicleId == Guid.Empty)
        {
            return;
        }

        _body.Add(NativeTheme.Eyebrow("Vehicle locations"));
        if (item.VehicleLocations.Count == 0)
        {
            _body.Add(NativeTheme.Body("No locations are defined inside this vehicle.", NativeTheme.Muted));
        }
        else
        {
            VerticalStackLayout locations = new() { Spacing = 10 };
            foreach (WorkspaceLocationItemState location in item.VehicleLocations)
            {
                locations.Add(NativeTheme.Metric(
                    string.IsNullOrEmpty(location.Name) ? "Unnamed location" : location.Name,
                    string.IsNullOrEmpty(location.Notes) ? "No notes" : location.Notes));
            }
            _body.Add(NativeTheme.Card(locations));
        }

        long contentRevision = Coordinator.State.ContentRevision;
        _body.Add(NativeTheme.NavigationRow(
            "Add location to vehicle",
            $"Create a location inside {item.Label}",
            () => Navigation.PushAsync(new VehicleLocationAddPage(
                Coordinator,
                workspaceId,
                contentRevision,
                vehicleId,
                item.Label)),
            automationId: $"vehicle-location-open-add-{vehicleId:N}"));
    }

    private string TargetToken() => Token(_target.NestedItemId ?? _target.ItemId);

    internal static bool TargetsMatch(WorkspaceCollectionItemTarget left, WorkspaceCollectionItemTarget right)
        => left.Kind == right.Kind
            && left.NestedKind == right.NestedKind
            && string.Equals(left.ItemId, right.ItemId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.NestedItemId, right.NestedItemId, StringComparison.OrdinalIgnoreCase);

    internal static string Token(string value)
        => new(value.Trim().ToLowerInvariant().Select(character => char.IsLetterOrDigit(character) ? character : '-').ToArray());
}

internal sealed class NestedCollectionAddPage : NativePageBase
{
    private readonly WorkspaceCollectionItemTarget _parent;
    private readonly WorkspaceNestedCollectionKind _nestedKind;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };
    private Entry _name = null!;
    private Entry _category = null!;
    private Entry _source = null!;
    private Editor _notes = null!;
    private Entry _customName = null!;
    private Entry _rating = null!;
    private Entry? _quantity;
    private Switch _equipped = null!;
    private Switch _wireless = null!;

    public NestedCollectionAddPage(
        RunnerSessionCoordinator coordinator,
        WorkspaceCollectionItemTarget parent,
        WorkspaceNestedCollectionKind nestedKind) : base(coordinator)
    {
        _parent = parent with { NestedKind = null, NestedItemId = null };
        _nestedKind = nestedKind;
        Title = $"Add {RunnerSessionCoordinator.HumanizeId(nestedKind.ToString())}";
        AutomationId = $"nested-add-{CollectionItemEditorPage.Token(nestedKind.ToString())}";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow("Child item"));
        _body.Add(NativeTheme.Title(Title));
        _name = AddTextField("Name · required", "nested-name");
        _category = AddTextField("Category", "nested-category");
        _source = AddTextField("Source", "nested-source");
        _notes = NativeTheme.TextArea("nested-notes", string.Empty);
        _body.Add(NativeTheme.FieldLabel("Notes"));
        _body.Add(NativeTheme.Card(_notes, new Thickness(12, 6)));
        _customName = AddTextField("Custom name", "nested-custom-name");
        _rating = AddTextField("Rating", "nested-rating", "0", Keyboard.Numeric);
        _quantity = _nestedKind == WorkspaceNestedCollectionKind.Gear
            ? AddTextField("Quantity", "nested-quantity", "1", Keyboard.Numeric)
            : null;
        _equipped = AddToggle("Equipped", "nested-equipped", true);
        _wireless = AddToggle("Wireless enabled", "nested-wireless", false);

        Button save = NativeTheme.PrimaryButton("Add item");
        save.AutomationId = "nested-save";
        save.Clicked += async (_, _) => await RunAsync(SaveAsync);
        _body.Add(save);
    }

    private Entry AddTextField(string label, string automationId, string value = "", Keyboard? keyboard = null)
    {
        _body.Add(NativeTheme.FieldLabel(label));
        Entry entry = NativeTheme.TextField(automationId, value);
        if (keyboard is not null)
        {
            entry.Keyboard = keyboard;
        }

        _body.Add(entry);
        return entry;
    }

    private Switch AddToggle(string label, string automationId, bool value)
    {
        Switch toggle = new() { AutomationId = automationId, IsToggled = value, OnColor = NativeTheme.Signal };
        Grid row = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        row.Add(NativeTheme.FieldLabel(label), 0, 0);
        row.Add(toggle, 1, 0);
        _body.Add(NativeTheme.Card(row));
        return toggle;
    }

    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(_name.Text))
        {
            await DisplayAlertAsync("Name required", "Enter a name before adding this item.", "OK");
            return;
        }

        if (!int.TryParse(_rating.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int rating)
            || rating is < 0 or > 1000)
        {
            await DisplayAlertAsync("Invalid rating", "Enter a whole number from 0 to 1000.", "OK");
            return;
        }

        decimal quantity = 1m;
        if (_quantity is not null
            && (!decimal.TryParse(_quantity.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out quantity)
                || quantity <= 0m
                || quantity > 1_000_000m))
        {
            await DisplayAlertAsync("Invalid quantity", "Enter a value greater than 0 and no greater than 1000000.", "OK");
            return;
        }

        await Coordinator.ApplyCollectionMutationAsync(new WorkspaceAddNestedCollectionItemRequest(
            _parent,
            _nestedKind,
            new WorkspaceNestedItemDraft(
                Name: _name.Text.Trim(),
                Category: EmptyToNull(_category.Text),
                Source: EmptyToNull(_source.Text),
                Notes: EmptyToNull(_notes.Text),
                CustomName: EmptyToNull(_customName.Text),
                Rating: rating,
                Quantity: quantity,
                Equipped: _equipped.IsToggled,
                WirelessEnabled: _wireless.IsToggled)));
        if (Coordinator.State.Error is null)
        {
            await Navigation.PopAsync();
        }
    }

    private static string? EmptyToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
