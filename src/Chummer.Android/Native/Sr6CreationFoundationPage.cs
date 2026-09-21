using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Android.Native;

/// <summary>SR6 pending choices only. Core owns all legality, budgets and writes.</summary>
internal sealed class Sr6CreationFoundationPage : NativePageBase
{
    private readonly VerticalStackLayout _body = new() { Padding = new Thickness(20, 18, 20, 40), Spacing = 14 };
    private readonly OwnerContextStamp? _owner;
    private readonly CharacterWorkspaceId? _workspace;
    private Sr6CreationFoundationState? _state;
    private Sr6CreationFoundationSelection? _selection;
    private Sr6CreationFoundationPreview? _preview;
    private Sr6CreationFoundationCommit? _commit;
    private IReadOnlyList<string> _blockers = [];
    private long _render;
    private bool _busy, _halted;
    private Label? _status;
    private Button? _confirm;
    private VerticalStackLayout? _review;

    internal Sr6CreationFoundationPage(RunnerSessionCoordinator coordinator) : base(coordinator)
    {
        _owner = coordinator.State.DisplayOwnerContext;
        _workspace = coordinator.State.WorkspaceId;
        Title = Sr6CreationCopy.Text("Title");
        AutomationId = "sr6-foundation-page";
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
        _body.IsEnabled = !_busy;
        _body.Add(NativeTheme.Title(Sr6CreationCopy.Text("Title")));
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
            _selection = selection;
            _preview = null;
            _commit = null;
            _blockers = [];
            if (_confirm is not null) _confirm.IsEnabled = false;
            _review?.Clear();
            _status.Text = Sr6CreationCopy.Text("Changed");
            // Do not rebuild the Picker visual tree inside SelectedIndexChanged.
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
