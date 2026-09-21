using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using System.Globalization;

namespace Chummer.Android.Native;

/// <summary>SR6 pending choices only. Core owns all legality, budgets and writes.</summary>
internal sealed class Sr6CreationFoundationPage : NativePageBase
{
    private readonly VerticalStackLayout _body = new() { Padding = new Thickness(20, 18, 20, 40), Spacing = 14 };
    private readonly OwnerContextStamp? _owner;
    private readonly CharacterWorkspaceId? _workspace;
    private readonly bool _attributesMode;
    private Sr6CreationFoundationState? _state;
    private Sr6CreationFoundationSelection? _selection;
    private Sr6CreationFoundationPreview? _preview;
    private Sr6CreationFoundationCommit? _commit;
    private IReadOnlyList<string> _blockers = [];
    private long _render;
    private bool _busy, _halted;
    private Label? _status;
    private Button? _confirm;
    private Button? _attributes;
    private VerticalStackLayout? _review;

    internal Sr6CreationFoundationPage(RunnerSessionCoordinator coordinator, bool attributesMode = false) : base(coordinator)
    {
        _attributesMode = attributesMode;
        _owner = coordinator.State.DisplayOwnerContext;
        _workspace = coordinator.State.WorkspaceId;
        Title = Sr6CreationCopy.Text(attributesMode ? "AttributeTitle" : "Title");
        AutomationId = attributesMode ? "sr6-attributes-page" : "sr6-foundation-page";
        Content = new ScrollView { Content = _body };
    }

    private bool FrameCurrent => Coordinator.State.WorkspaceId == _workspace
        && Coordinator.State.DisplayOwnerContext == _owner && Coordinator.State.Session.OwnerContext == _owner
        && Coordinator.CanOpenSr6Foundation();
    private bool Ready => !_halted && FrameCurrent && _state is { } state && Coordinator.IsSr6FoundationStateCurrent(state);

    protected override void OnAppearing()
    {
        _body.IsEnabled = false;
        base.OnAppearing();
    }

    protected override void OnDisappearing()
    {
        _body.IsEnabled = false;
        _render++;
        base.OnDisappearing();
    }

    protected override async Task PrepareForAppearanceRefreshAsync(CancellationToken cancellationToken)
    {
        long appearance = CaptureAppearanceGeneration();
        bool Current() => FrameCurrent && IsCurrentAppearanceGeneration(appearance);
        if (_halted || !Current()) return;
        var previous = _state;
        var result = await Coordinator.LoadSr6FoundationAsync(cancellationToken, Current);
        if (!Current()) return;
        _preview = null;
        if (result.Value is not { } state) { _blockers = result.Blockers; _state = null; return; }
        if (previous is not null && previous.Binding != state.Binding)
        { _halted = true; _blockers = [Sr6CreationFoundationBlockers.StaleBinding]; return; }
        _state = state;
        if (_selection is null && state.Selection is { } saved)
            _selection = saved.Selection with { Assignments = saved.Selection.Assignments.ToArray() };
        _blockers = [];
    }

    protected override void Refresh()
    {
        long render = ++_render;
        long appearance = CaptureAppearanceGeneration();
        _body.Clear();
        _attributes = null;
        _body.IsEnabled = !_busy;
        _body.Add(NativeTheme.Title(Title));
        _body.Add(NativeTheme.Body(Sr6CreationCopy.Text("Scope"), NativeTheme.Muted));
        _status = NativeTheme.Body(string.Join("\n", _blockers.Select(Sr6CreationCopy.Blocker)), NativeTheme.Danger);
        _status.AutomationId = "sr6-foundation-status";
        _body.Add(_status);
        if (_commit is { } committed && FrameCurrent && Coordinator.CanDisplaySr6FoundationCommit(committed))
            _body.Add(NativeTheme.Body(Sr6CreationCopy.Text(_state is null ? "SavedReopen" : "Saved")));
        if (!Ready || _state is not { } state)
        {
            _body.Add(NativeTheme.Body(Sr6CreationCopy.Text("Stale"), NativeTheme.Muted));
            return;
        }
        bool Current() => !_busy && render == _render && IsCurrentAppearanceGeneration(appearance) && Ready;
        _body.Add(NativeTheme.Body(state.BuildMethod + " · " + CreationKarmaCopy.Binding(
            state.Binding.ContentRevision, state.Binding.SavedRevision), NativeTheme.Muted));
        _selection ??= new("", "", CharacterCreationPriorityCategoryIds.Ordered.Select(id => new Sr6CreationPriorityChoice(id, "")).ToArray());

        if (_attributesMode)
        {
            if (state.Selection is not { } savedFoundation || state.AttributeOptions is not { Count: 11 } options)
            { _body.Add(NativeTheme.Body(Sr6CreationCopy.Text("FoundationRequired"))); return; }
            _body.Add(NativeTheme.Body(Sr6CreationCopy.Budget(savedFoundation)));
            _body.Add(NativeTheme.Body(Sr6CreationCopy.Text("AttributeHelp"), NativeTheme.Muted));
            _selection = _selection with { Attributes = _selection.Attributes ?? new(
                options.Select(option => new Sr6CreationAttributeSpend(option.AttributeId, 0, 0)).ToArray()) };
            foreach (var option in options.Where(option => option.Maximum > 0))
            {
                _body.Add(NativeTheme.Body(Sr6CreationCopy.AttributeRange(option)));
                if (option.AllowsAttributePoints) AddSpendPicker(option, false, savedFoundation.Budget.AttributePoints);
                if (option.AllowsAdjustmentPoints) AddSpendPicker(option, true, savedFoundation.Budget.MetatypeAdjustmentPoints);
            }
        }
        else
        {
            if (state.Selection is { } saved)
            {
                bool SavedCurrent() => Current() && Sr6CreationFoundationIntegrity.Digest(_selection)
                    == Sr6CreationFoundationIntegrity.Digest(saved.Selection);
                var attributes = NativeTheme.PrimaryButton(Sr6CreationCopy.Text("AttributeOpen"));
                _attributes = attributes;
                attributes.AutomationId = "sr6-foundation-attributes";
                attributes.IsEnabled = SavedCurrent();
                attributes.Clicked += async (_, _) =>
                {
                    if (!SavedCurrent()) return;
                    await RunAsync(async () =>
                    {
                        if (SavedCurrent()) await Navigation.PushAsync(new Sr6CreationFoundationPage(Coordinator, attributesMode: true));
                    });
                };
                _body.Add(attributes);
                _body.Add(NativeTheme.Body(Sr6CreationCopy.Text("AttributeResetWarning"), NativeTheme.Muted));
            }
            foreach (string category in CharacterCreationPriorityCategoryIds.Ordered)
            {
                string captured = category;
                AddPicker("rank-" + category, Sr6CreationCopy.Label(category), ["A", "B", "C", "D", "E"],
                    _selection.Assignments.Single(item => item.CategoryId == category).Rank,
                    id => id, value => Change(_selection with { Assignments = _selection.Assignments.Select(item =>
                        item.CategoryId == captured ? item with { Rank = value } : item).ToArray() }));
            }
            AddPicker("metatype", Sr6CreationCopy.Text("Metatype"), state.Metatypes.Select(item => item.Id).ToArray(),
                _selection.MetatypeId, Sr6CreationCopy.Label, value => Change(_selection with { MetatypeId = value }));
            AddPicker("talent", Sr6CreationCopy.Text("Talent"), state.Talents.Select(item => item.Id).ToArray(),
                _selection.TalentId, Sr6CreationCopy.Label, value => Change(_selection with { TalentId = value }));
        }

        var previewButton = NativeTheme.PrimaryButton(Sr6CreationCopy.Text("Preview"));
        previewButton.AutomationId = "sr6-foundation-preview";
        previewButton.Clicked += async (_, _) =>
        {
            if (!Current()) return;
            await RunAsync(async () =>
            {
                if (!Current()) return;
                _busy = true;
                _body.IsEnabled = false;
                try
                {
                    var result = await Coordinator.PreviewSr6FoundationAsync(state, _selection,
                        isCurrentPage: () => render == _render && IsCurrentAppearanceGeneration(appearance) && FrameCurrent);
                    if (render != _render || !IsCurrentAppearanceGeneration(appearance) || !FrameCurrent) return;
                    _preview = result.Value;
                    _blockers = result.Blockers;
                }
                finally { _busy = false; }
            });
        };
        _body.Add(previewButton);
        _review = new VerticalStackLayout { Spacing = 8, AutomationId = "sr6-foundation-review" };
        _body.Add(_review);
        if (_preview is { } quote && Coordinator.IsSr6FoundationPreviewCurrent(quote))
        {
            _review.Add(NativeTheme.Body(Sr6CreationCopy.Budget(quote)));
            if (quote.Attributes is { } allocation)
            {
                _review.Add(NativeTheme.Body(Sr6CreationCopy.AttributeBudget(allocation)));
                foreach (var value in allocation.Values.Where(value => value.Maximum > 0))
                    _review.Add(NativeTheme.Body(Sr6CreationCopy.AttributeValue(value)));
            }
            _review.Add(NativeTheme.Body(string.Join(" · ", quote.SourceAnchorIds), NativeTheme.Muted));
            _confirm = NativeTheme.PrimaryButton(Sr6CreationCopy.Text("Confirm"));
            _confirm.AutomationId = "sr6-foundation-confirm";
            _confirm.Clicked += async (_, _) =>
            {
                if (!Current() || !ReferenceEquals(_preview, quote)) return;
                await RunAsync(async () =>
                {
                    if (!Current() || !ReferenceEquals(_preview, quote)) return;
                    _busy = true;
                    _body.IsEnabled = false;
                    try
                    {
                        var result = await Coordinator.ConfirmSr6FoundationAsync(quote, true,
                            isCurrentPage: () => IsCurrentAppearanceGeneration(appearance) && FrameCurrent);
                        if (!IsCurrentAppearanceGeneration(appearance) || !FrameCurrent) return;
                        _preview = null;
                        _blockers = result.Blockers;
                        _commit = result.Commit;
                        _state = result.State;
                        // Unknown outcomes are resolved by reopening, never a new automatic write.
                        _halted = result.State is null;
                    }
                    finally { _busy = false; }
                });
            };
            _review.Add(_confirm);
        }

        void Change(Sr6CreationFoundationSelection selection)
        {
            if (!Current()) return;
            bool cleared = !_attributesMode && _selection.Attributes is not null;
            if (!_attributesMode) selection = selection with { Attributes = null };
            _selection = selection;
            _preview = null;
            _commit = null;
            _blockers = [];
            if (_confirm is not null) _confirm.IsEnabled = false;
            if (_attributes is not null) _attributes.IsEnabled = false;
            _review?.Clear();
            _status.Text = Sr6CreationCopy.Text(cleared ? "AttributeResetWarning" : "Changed");
            // Do not rebuild the Picker visual tree inside SelectedIndexChanged.
        }

        void AddSpendPicker(Sr6CreationAttributeOption option, bool adjustment, int budget)
        {
            string id = option.AttributeId;
            var spend = _selection.Attributes!.Allocations.Single(row => row.AttributeId == id);
            int selected = adjustment ? spend.AdjustmentPoints : spend.AttributePoints;
            var options = Enumerable.Range(0, Math.Min(option.Maximum - option.BaseValue, budget) + 1)
                .Select(value => value.ToString(CultureInfo.InvariantCulture)).ToArray();
            AddPicker((adjustment ? "adjustment-" : "normal-") + id,
                Sr6CreationCopy.Text(adjustment ? "AdjustmentPoints" : "AttributePoints"), options,
                selected.ToString(CultureInfo.InvariantCulture), value => value, value =>
                {
                    int points = int.Parse(value, CultureInfo.InvariantCulture);
                    Change(_selection with { Attributes = new(_selection.Attributes!.Allocations.Select(row => row.AttributeId != id ? row
                        : adjustment ? row with { AdjustmentPoints = points } : row with { AttributePoints = points }).ToArray()) });
                });
        }

        void AddPicker(string id, string title, string[] options, string selected,
            Func<string, string> label, Action<string> change)
        {
            _body.Add(NativeTheme.Body(title));
            var picker = new Picker { Title = title, AutomationId = "sr6-foundation-" + id };
            foreach (string option in options) picker.Items.Add(label(option));
            picker.SelectedIndex = Array.IndexOf(options, selected);
            picker.SelectedIndexChanged += (_, _) =>
            {
                if (Current() && picker.SelectedIndex >= 0 && picker.SelectedIndex < options.Length)
                    change(options[picker.SelectedIndex]);
            };
            _body.Add(picker);
        }
    }
}
