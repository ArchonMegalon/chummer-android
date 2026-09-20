using System.Globalization;
using Chummer.Contracts.Characters;

namespace Chummer.Android.Native;

/// <summary>Explicit roll, source-owned review and one atomic Career transition.</summary>
internal sealed class CreationKarmaCompletionPage : NativePageBase
{
    private readonly CharacterCreationKarmaMetatypeQuote _foundation;
    private readonly VerticalStackLayout _body = new() { Padding = new Thickness(20, 18, 20, 40), Spacing = 14 };
    private CharacterCreationStartingNuyenSource? _source;
    private CharacterCreationFinalizationReview? _review;
    private CharacterCreationFinalizationReceipt? _receipt;
    private IReadOnlyList<string> _blockers = [];
    private string _roll = string.Empty;
    private long _render, _inputVersion;
    private bool _attempted;

    internal CreationKarmaCompletionPage(RunnerSessionCoordinator coordinator, CharacterCreationKarmaMetatypeQuote foundation)
        : base(coordinator)
    {
        _foundation = foundation;
        Title = CreationKarmaCopy.Finish;
        AutomationId = "creation-karma-completion";
        Content = new ScrollView { Content = _body };
    }

    protected override void OnDisappearing()
    {
        _render++;
        _body.IsEnabled = false;
        base.OnDisappearing();
    }

    protected override async Task PrepareForAppearanceRefreshAsync(CancellationToken ct)
    {
        if (_attempted || _receipt is not null) return;
        long appearance = CaptureAppearanceGeneration();
        _body.IsEnabled = false;
        _review = null;
        _source = null;
        _body.Clear();
        _body.Add(NativeTheme.Body(CreationKarmaCopy.Loading));
        var result = await Coordinator.LoadKarmaCompletionCashAsync(_foundation, ct,
            () => IsCurrentAppearanceGeneration(appearance));
        if (!IsCurrentAppearanceGeneration(appearance)) return;
        _source = result.Value;
        _blockers = result.Blockers;
    }

    protected override void Refresh()
    {
        long render = ++_render, appearance = CaptureAppearanceGeneration();
        _body.Clear();
        _body.IsEnabled = true;
        _body.Add(NativeTheme.Title(Title));
        if (_receipt is { } receipt)
        {
            if (!Coordinator.CanDisplayCreationFinalizationReceipt(receipt))
            { _body.Add(NativeTheme.Body(CreationKarmaCopy.Stale, NativeTheme.Danger)); return; }
            bool ready = Coordinator.IsCreationFinalizationReceiptCurrent(receipt);
            var status = NativeTheme.Body(ready ? CreationKarmaCopy.CareerReady : CreationKarmaCopy.CareerReopen);
            status.AutomationId = "karma-completion-receipt";
            _body.Add(status);
            _body.Add(NativeTheme.Body(CreationKarmaCopy.Binding(receipt.ContentRevision, receipt.SavedRevision)));
            AddBlockers();
            var done = NativeTheme.PrimaryButton(CreationKarmaCopy.OpenCareer);
            done.AutomationId = "karma-completion-open-career";
            done.IsEnabled = ready;
            done.Clicked += async (_, _) => await RunAsync(async () =>
            {
                if (render == _render && IsCurrentAppearanceGeneration(appearance)
                    && Coordinator.IsCreationFinalizationReceiptCurrent(receipt)) await Navigation.PopToRootAsync();
            });
            _body.Add(done);
            return;
        }
        if (_attempted || !Coordinator.IsCreationKarmaPreviewCurrent(_foundation))
        { _body.Add(NativeTheme.Body(CreationKarmaCopy.Stale, NativeTheme.Danger)); AddBlockers(); return; }
        _body.Add(NativeTheme.Body(CreationKarmaCopy.CompletionHelp, NativeTheme.Muted));
        if (_source is not { } source) { AddBlockers(); return; }
        _body.Add(NativeTheme.Body(CreationKarmaCopy.StartingCash(source.Name, source.Dice, source.Multiplier)));
        _body.Add(NativeTheme.Body(source.SourceBook + " · " + source.Page, NativeTheme.Muted));
        _body.Add(NativeTheme.Body(CreationKarmaCopy.DiceTotal));
        var input = new Entry { Text = _roll, Keyboard = Keyboard.Numeric, AutomationId = "karma-completion-roll" };
        var preview = NativeTheme.SecondaryButton(CreationKarmaCopy.PreviewCompletion);
        preview.AutomationId = "karma-completion-preview";
        var details = new VerticalStackLayout { Spacing = 10 };
        var confirm = NativeTheme.PrimaryButton(CreationKarmaCopy.ConfirmCompletion);
        confirm.AutomationId = "karma-completion-confirm";
        void Validate()
        {
            preview.IsEnabled = TryRoll(out _);
            confirm.IsEnabled = _review is { } review && Coordinator.IsKarmaCompletionCurrent(review);
        }
        input.TextChanged += (_, _) =>
        {
            if (!Current()) return;
            _roll = input.Text ?? string.Empty;
            _inputVersion++;
            _review = null;
            details.Clear();
            Validate();
        };
        preview.Clicked += async (_, _) => await RunAsync(async () =>
        {
            await Task.Yield();
            if (!Current() || !TryRoll(out int dice)) return;
            long version = _inputVersion;
            _body.IsEnabled = false;
            var result = await Coordinator.ReviewKarmaCompletionAsync(_foundation, source, dice, default,
                () => Current() && version == _inputVersion);
            if (!Current() || version != _inputVersion) return;
            _review = result.Value;
            _blockers = result.Blockers;
        });
        confirm.Clicked += async (_, _) => await RunAsync(async () =>
        {
            await Task.Yield();
            if (!Current() || _review is not { } review || !Coordinator.IsKarmaCompletionCurrent(review)) return;
            _attempted = true;
            _body.IsEnabled = false;
            var result = await Coordinator.ConfirmKarmaCompletionAsync(review, true, default,
                () => IsCurrentAppearanceGeneration(appearance));
            // Retain a known receipt even if navigation/cancellation prevents
            // refresh. No second confirmation is offered for this intent.
            _receipt = result.Value;
            _blockers = result.Blockers;
        });
        Validate();
        _body.Add(input);
        _body.Add(preview);
        if (_review is { Plan: { } plan } current && Coordinator.IsKarmaCompletionCurrent(current))
        {
            details.Add(NativeTheme.Body(CreationKarmaCopy.CompletionTotals(plan.KarmaRemaining, plan.StartingNuyen, plan.NuyenRemaining)));
            foreach (var delta in current.OrderedDeltas.OrderBy(item => item.Order))
            {
                var line = NativeTheme.Body(CreationAllocationStrings.Format("Karma.Delta", "{0}: {1} → {2}",
                    delta.TargetId, delta.BeforeValue ?? "—", delta.AfterValue ?? "—"));
                line.AutomationId = "karma-completion-delta-" + delta.Order.ToString(CultureInfo.InvariantCulture);
                details.Add(line);
                if (delta.KarmaCost != 0 || delta.NuyenCost != 0)
                    details.Add(NativeTheme.Body(CreationKarmaCopy.DeltaCost(delta.KarmaCost, delta.NuyenCost), NativeTheme.Muted));
                if (delta.SourceAnchorIds.Count > 0)
                    details.Add(NativeTheme.Body(string.Join(" · ", delta.SourceAnchorIds), NativeTheme.Muted));
            }
        }
        _body.Add(details);
        _body.Add(confirm);
        AddBlockers();
        bool Current() => render == _render && IsCurrentAppearanceGeneration(appearance)
            && !_attempted && Coordinator.IsCreationKarmaPreviewCurrent(_foundation);
        bool TryRoll(out int dice) => int.TryParse(_roll, NumberStyles.None, CultureInfo.InvariantCulture, out dice);
    }

    private void AddBlockers()
    {
        foreach (string blocker in _blockers)
        {
            var label = NativeTheme.Body(blocker, NativeTheme.Danger);
            label.AutomationId = "karma-completion-blocker-" + blocker;
            _body.Add(label);
        }
    }
}
