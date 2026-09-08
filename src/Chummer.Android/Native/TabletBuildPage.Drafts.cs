using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed partial class TabletBuildPage
{
    private TabletInspectorDraft? _renderedDraft;
    private long _draftLease;
    private bool _draftConflict;
    private Label? _draftStatus;
    private CharacterWorkspaceId? _selectionWorkspace;
    private string? _selectionSection;

    private void RestoreInspectorSelection()
    {
        CharacterOverviewState state = Coordinator.State;
        if (_selectionWorkspace == state.WorkspaceId && _selectionSection == state.ActiveSectionId) return;
        _selectionWorkspace = state.WorkspaceId;
        _selectionSection = state.ActiveSectionId;
        TabletInspectorKey? selected = state.WorkspaceId is { } workspace && state.ActiveSectionId is { } section
            ? Coordinator.TabletInspectorDrafts.Selection(workspace, section) : null;
        _selectedTarget = selected?.Item;
        _selectedConditionTrack = selected?.Track;
        _selectedAttributeName = selected?.Attribute;
    }

    private IEnumerable<InputView> InspectorTextInputs()
        => _textInputs.Values.Concat(new InputView?[]
        {
            _ratingInput, _quantityInput, _contactConnectionInput, _contactLoyaltyInput
        }.OfType<InputView>());

    private IEnumerable<Picker> InspectorPickers()
        => new[] { _attributeBasePicker, _attributeKarmaPicker, _conditionFilledPicker,
            _vehiclePhysicalDamagePicker, _matrixDamagePicker }.OfType<Picker>();

    private TabletInspectorValues ReadInspectorValues()
        => new(
            InspectorTextInputs().ToDictionary(input => input.AutomationId, input => (string?)input.Text, StringComparer.Ordinal),
            _toggleInputs.Values.ToDictionary(input => input.AutomationId, input => input.IsToggled, StringComparer.Ordinal),
            InspectorPickers().ToDictionary(input => input.AutomationId,
                input => input.SelectedItem as string, StringComparer.Ordinal));

    private void CaptureInspectorDraft()
    {
        if (_departed || _renderedDraft is null || _draftConflict
            || !Coordinator.TabletInspectorDrafts.Owns(_draftLease)) return;
        _renderedDraft = _renderedDraft with { Values = ReadInspectorValues() };
        _renderedDraft = Coordinator.TabletInspectorDrafts.Retain(_renderedDraft, _draftLease);
        if (_draftStatus is not null) _draftStatus.IsVisible = _renderedDraft.HasChanges;
    }

    private bool CurrentDraftAuthorityMatches()
    {
        if (_renderedDraft is not { } draft || !Coordinator.TabletInspectorDrafts.Owns(_draftLease)) return false;
        CharacterOverviewState current = Coordinator.State;
        object? projection;
        if (draft.Key.Item is { } target)
        {
            WorkspaceCollectionItemEditorState[] matches = current.ActiveCollectionEditor?.Items.Where(item =>
                CollectionItemEditorPage.TargetsMatch(item.Target, target)).ToArray() ?? [];
            projection = matches.Length == 1 ? matches[0] : null;
        }
        else if (draft.Key.Track is { } track)
            projection = current.ActiveConditionMonitor is { } monitor
                && monitor.Tracks.Count(item => item.Track == track) == 1 ? monitor : null;
        else if (draft.Key.Attribute is { } attribute)
        {
            IReadOnlyList<AttributeWorkbenchRow> attributes = CurrentAttributes();
            projection = attributes.Count(item => item.AttributeName == attribute) == 1 ? attributes : null;
        }
        else projection = null;
        return projection is not null && draft.Authority == TabletInspectorDraftStore.Authority(current, projection);
    }

    private void BindInspectorDraft(CharacterOverviewState expected, long generation,
        TabletInspectorKey key, object projection)
    {
        _draftLease = Coordinator.TabletInspectorDrafts.Acquire(key);
        TabletInspectorValues original = ReadInspectorValues();
        string authority = TabletInspectorDraftStore.Authority(expected, projection);
        TabletInspectorDraft? retained = Coordinator.TabletInspectorDrafts.Find(key);
        _renderedDraft = retained ?? new(key, authority, original, original);
        _draftConflict = retained is not null && retained.Authority != authority;
        _draftStatus = null;
        if (_draftConflict)
        {
            AddDraftConflict(expected, generation, retained!);
            return;
        }

        if (retained is not null)
        {
            foreach (InputView input in InspectorTextInputs())
                if (retained.Values.Text.TryGetValue(input.AutomationId, out string? value)) input.Text = value;
            foreach (Switch input in _toggleInputs.Values)
                if (retained.Values.Toggles.TryGetValue(input.AutomationId, out bool value)) input.IsToggled = value;
            foreach (Picker input in InspectorPickers())
                if (retained.Values.Pickers.TryGetValue(input.AutomationId, out string? value))
                    input.SelectedIndex = value is null ? -1 : input.ItemsSource.IndexOf(value);
        }

        _draftStatus = NativeTheme.Body(PhoneStrings.Get("TabletDraftPending",
            "Unapplied changes retained in this session."), NativeTheme.Muted);
        _draftStatus.AutomationId = "tablet-inspector-draft-pending";
        _draftStatus.IsVisible = _renderedDraft.HasChanges;
        _inspector.Insert(1, _draftStatus);
        Button discard = NativeTheme.SecondaryButton(PhoneStrings.Get("TabletDraftDiscard", "Discard this draft"));
        discard.AutomationId = "tablet-inspector-discard-draft";
        discard.Clicked += async (_, _) => await DiscardInspectorDraftAsync(expected, generation);
        _inspector.Add(discard);

        // Capture only from this render. Detached controls cannot overwrite a new
        // item's retained input, and restoration above cannot look like a user edit.
        void Changed()
        {
            if (!_departed && generation == _inspectorGeneration) CaptureInspectorDraft();
        }
        foreach (InputView input in InspectorTextInputs()) input.TextChanged += (_, _) => Changed();
        foreach (Switch input in _toggleInputs.Values) input.Toggled += (_, _) => Changed();
        foreach (Picker input in InspectorPickers()) input.SelectedIndexChanged += (_, _) => Changed();
    }

    private void AddDraftConflict(CharacterOverviewState expected, long generation, TabletInspectorDraft retained)
    {
        // Current values stay visible; obsolete input is displayed separately and
        // never clamped, truncated into current controls, or silently reapplied.
        foreach (InputView input in InspectorTextInputs()) input.IsEnabled = false;
        foreach (Switch input in _toggleInputs.Values) input.IsEnabled = false;
        foreach (Picker input in InspectorPickers()) input.IsEnabled = false;
        foreach (Button button in InspectorButtons(_inspector)) button.IsEnabled = false;

        VerticalStackLayout conflict = new() { Spacing = 10, AutomationId = "tablet-inspector-draft-conflict" };
        conflict.Add(NativeTheme.Body(PhoneStrings.Get("TabletDraftConflict",
            "This runner or its rules changed. Your previous input is retained below and has not been applied. Discard it explicitly before editing the current values."),
            NativeTheme.Danger));
        foreach ((string field, string? value) in retained.Values.Text)
            if (!retained.Original.Text.TryGetValue(field, out string? original) || value != original)
                conflict.Add(DraftValue(field, value));
        foreach ((string field, bool value) in retained.Values.Toggles)
            if (!retained.Original.Toggles.TryGetValue(field, out bool original) || value != original)
                conflict.Add(DraftValue(field, value.ToString()));
        foreach ((string field, string? value) in retained.Values.Pickers)
            if (!retained.Original.Pickers.TryGetValue(field, out string? original) || value != original)
                conflict.Add(DraftValue(field, value));
        Button discard = NativeTheme.SecondaryButton(PhoneStrings.Get("TabletDraftDiscard", "Discard this draft"));
        discard.AutomationId = "tablet-inspector-discard-draft";
        discard.Clicked += async (_, _) => await DiscardInspectorDraftAsync(expected, generation);
        conflict.Add(discard);
        _inspector.Insert(1, NativeTheme.Card(conflict));
    }

    private static Label DraftValue(string field, string? value)
    {
        Label label = NativeTheme.Body($"{RunnerSessionCoordinator.HumanizeId(field)}: {value ?? "—"}");
        label.AutomationId = $"tablet-retained-{field}";
        return label;
    }

    private static IEnumerable<Button> InspectorButtons(Element root)
    {
        if (root is Button button) yield return button;
        IEnumerable<Element> children = root switch
        {
            Layout layout => layout.Children.OfType<Element>(),
            Border { Content: { } content } => [content],
            _ => []
        };
        foreach (Element child in children)
        foreach (Button descendant in InspectorButtons(child)) yield return descendant;
    }

    private Task DiscardInspectorDraftAsync(CharacterOverviewState expected, long generation)
        => RunWithConditionalRefreshAsync(async () =>
        {
            if (generation != _inspectorGeneration || !IsCurrentView(expected)
                || !Coordinator.TabletInspectorDrafts.Owns(_draftLease)) return false;
            CaptureInspectorDraft();
            TabletInspectorDraft? draft = _renderedDraft;
            if (draft is not { HasChanges: true }) return false;
            if (!await _confirm(
                PhoneStrings.Get("TabletDraftDiscardTitle", "Discard unapplied changes?"),
                PhoneStrings.Get("TabletDraftDiscardPrompt",
                    "Only this item's retained input will be removed. The saved runner and other drafts will not change."),
                PhoneStrings.Get("TabletDraftDiscard", "Discard this draft"),
                PhoneStrings.Get("CommonCancel", "Cancel"))) return false;
            if (generation != _inspectorGeneration || !IsCurrentView(expected)
                || _renderedDraft?.Key != draft.Key
                || !Coordinator.TabletInspectorDrafts.DiscardIfUnchanged(draft, _draftLease)) return false;
            _renderedDraft = null; // Refresh must not recapture explicitly discarded controls.
            _draftConflict = false;
            return true;
        });

    private void ForgetObservedAppliedDraft(CharacterOverviewState expected, TabletInspectorDraft? draft,
        Func<CharacterOverviewState, bool> matches)
    {
        CharacterOverviewState current = Coordinator.State;
        if (draft is null || current.Error is not null || current.IsBusy
            || current.WorkspaceId != expected.WorkspaceId
            || current.ContentRevision <= expected.ContentRevision || !matches(current)
            || !Coordinator.TabletInspectorDrafts.DiscardObservedIfUnchanged(draft)) return;
        if (_renderedDraft is { } rendered && rendered.Key == draft.Key
            && rendered.Authority == draft.Authority && rendered.Values.SameAs(draft.Values))
        {
            _renderedDraft = null;
            _draftConflict = false;
        }
    }

    private static bool CollectionPatchObserved(CharacterOverviewState state, WorkspacePatchCollectionItemRequest request)
    {
        WorkspaceCollectionItemEditorState[] items = state.ActiveCollectionEditor?.Items.Where(item =>
            CollectionItemEditorPage.TargetsMatch(item.Target, request.Target)).ToArray() ?? [];
        if (items.Length != 1) return false;
        WorkspaceCollectionItemEditorState item = items[0];
        return (request.TextValues is null || request.TextValues.All(pair =>
                item.TextValues.Count(value => value.Field == pair.Key) == 1
                && item.TextValues.Single(value => value.Field == pair.Key).Value == (pair.Value ?? "")))
            && (request.ToggleValues is null || request.ToggleValues.All(pair =>
                item.ToggleValues.Count(value => value.Field == pair.Key) == 1
                && item.ToggleValues.Single(value => value.Field == pair.Key).Value == pair.Value))
            && (request.Rating is null || item.Rating?.Value == request.Rating)
            && (request.Quantity is null || item.Quantity?.Value == request.Quantity)
            && (request.ContactConnection is null || item.Contact?.Connection == request.ContactConnection)
            && (request.ContactLoyalty is null || item.Contact?.Loyalty == request.ContactLoyalty)
            && (request.VehiclePhysicalDamage is null || item.PhysicalConditionMonitor?.Filled == request.VehiclePhysicalDamage)
            && new[] { request.VehicleMatrixDamage, request.GearMatrixDamage, request.ArmorMatrixDamage,
                request.WeaponMatrixDamage, request.CyberwareMatrixDamage }
                .All(value => value is null || item.MatrixConditionMonitor?.Filled == value);
    }

    private static bool ConditionEditObserved(CharacterOverviewState state, ConditionMonitorEditRequest request)
    {
        ConditionMonitorTrackState[] tracks = state.ActiveConditionMonitor?.Tracks
            .Where(track => track.Track == request.Track).ToArray() ?? [];
        return tracks.Length == 1 && tracks[0].Filled == request.Filled;
    }

    private static bool AttributeEditObserved(CharacterOverviewState state, string name, int baseValue, int karmaValue)
    {
        AttributeWorkbenchRow[] attributes = AttributeWorkbenchProjector
            .BuildRows(state.ActiveSectionId, state.ActiveSectionJson ?? "")
            .Where(row => string.Equals(row.AttributeName, name, StringComparison.OrdinalIgnoreCase)).ToArray();
        return attributes.Length == 1 && attributes[0].BaseValue == baseValue && attributes[0].KarmaValue == karmaValue;
    }
}
