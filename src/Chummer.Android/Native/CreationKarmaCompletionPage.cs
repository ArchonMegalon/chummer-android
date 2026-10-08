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
        _body.Add(NativeTheme.Body(CreationKarmaCopy.DiceTotalHelp, NativeTheme.Muted));
        var input = new Entry { Text = _roll, Keyboard = Keyboard.Numeric, AutomationId = "karma-completion-roll",
            TextColor = NativeTheme.Text, BackgroundColor = NativeTheme.Surface };
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
            List<Label> sources = [];
            bool showDetails = false;
            var toggle = NativeTheme.SecondaryButton(CreationFlowStrings.Get("Qualities.ShowDetails", "Show technical details"));
            toggle.AutomationId = "karma-completion-details";
            toggle.Clicked += (_, _) =>
            {
                if (!Current() || !ReferenceEquals(_review, current) || !ReferenceEquals(toggle.Parent, details)
                    || !Coordinator.IsKarmaCompletionCurrent(current)) return;
                showDetails = !showDetails;
                foreach (var label in sources) label.IsVisible = showDetails;
                toggle.Text = showDetails
                    ? CreationFlowStrings.Get("Qualities.HideDetails", "Hide technical details")
                    : CreationFlowStrings.Get("Qualities.ShowDetails", "Show technical details");
            };
            details.Add(toggle);
            foreach (var delta in current.OrderedDeltas.OrderBy(item => item.Order))
            {
                // Display the name already sealed into Core's review; never
                // resolve another catalog row or substitute the mutation target.
                var line = NativeTheme.Body(TargetLabel(delta) + ": " + ChangeLabel(delta));
                line.AutomationId = "karma-completion-delta-" + delta.Order.ToString(CultureInfo.InvariantCulture);
                details.Add(line);
                if (delta.KarmaCost != 0 || delta.NuyenCost != 0)
                    details.Add(NativeTheme.Body(CreationKarmaCopy.DeltaCost(delta.KarmaCost, delta.NuyenCost), NativeTheme.Muted));
                var technical = NativeTheme.Body(delta.TargetId
                    + $"\n{delta.Kind} · {delta.BeforeValue ?? "—"} → {delta.AfterValue ?? "—"}"
                    + (delta.SourceAnchorIds.Count > 0 ? "\n" + string.Join(" · ", delta.SourceAnchorIds) : string.Empty),
                    NativeTheme.Muted);
                technical.AutomationId = "karma-completion-source-" + delta.Order.ToString(CultureInfo.InvariantCulture);
                technical.IsVisible = false;
                sources.Add(technical);
                details.Add(technical);
            }
        }
        _body.Add(details);
        _body.Add(confirm);
        AddBlockers();
        bool Current() => render == _render && IsCurrentAppearanceGeneration(appearance)
            && !_attempted && Coordinator.IsCreationKarmaPreviewCurrent(_foundation);
        bool TryRoll(out int dice) => int.TryParse(_roll, NumberStyles.None, CultureInfo.InvariantCulture, out dice);
    }

    private static string TargetLabel(CharacterCreationFinalizationDelta delta)
    {
        if (!string.IsNullOrWhiteSpace(delta.TargetName)) return delta.TargetName;
        // These are typed scalar field identities, not catalog identifiers.
        // Reuse the same translated vocabulary as the other creation reviews.
        string? label = delta.Kind switch
        {
            CharacterCreationFinalizationDeltaKinds.MagicResonance => delta.TargetId switch
            {
                "magenabled" => CreationAllocationStrings.AttributeName("MAG"),
                "resenabled" => CreationAllocationStrings.AttributeName("RES"),
                "depenabled" => CreationAllocationStrings.AttributeName("DEP"),
                "magician" => CreationAllocationStrings.Get("Sr6.Option.magician", "Magician"),
                "adept" => CreationAllocationStrings.Get("Sr6.Option.adept", "Adept"),
                "technomancer" => CreationAllocationStrings.Get("Sr6.Option.technomancer", "Technomancer"),
                "spells-karma" => CreationKarmaCopy.Spells + " · Karma",
                "complex-forms-karma" => CreationKarmaCopy.ComplexForms + " · Karma",
                "magsplitadept" => CreationFlowStrings.Get("Magic.Mystic.Title", "Mystic Adept power points"),
                _ => null
            },
            CharacterCreationFinalizationDeltaKinds.Resources => delta.TargetId switch
            {
                "startingnuyen" => LifeCopy("CreationFunding", "Creation funds"),
                "resource-karma-rounding" => LifeCopy("ResourceRounding", "Resource Karma rounding"),
                "karma" => LifeCopy("CareerKarma", "Karma carried into Career"),
                "nuyen-carried" => LifeCopy("NuyenCarried", "Unspent nuyen carried into Career"),
                "lifestyle-starting-nuyen" => LifeCopy("StartingCash", "Rolled starting cash"),
                _ => null
            },
            CharacterCreationFinalizationDeltaKinds.Build => delta.TargetId switch
            {
                "contacts-karma" => LifeCopy("ContactKarma", "Contact Karma"),
                "qualities-karma-adjustment" => LifeCopy("QualityKarma", "Quality Karma adjustment"),
                _ => null
            },
            _ => null
        };
        return label ?? CreationFinalizationPage.TargetLabel(delta);

        static string LifeCopy(string key, string fallback) => CreationAllocationStrings.Get("LifeCompletion." + key, fallback);
    }

    private static string ChangeLabel(CharacterCreationFinalizationDelta delta)
    {
        if (delta.Kind == CharacterCreationFinalizationDeltaKinds.MagicResonance
            && delta.TargetId is "magenabled" or "resenabled" or "depenabled" or "magician" or "adept" or "technomancer"
            && delta.BeforeValue == "False" && delta.AfterValue == "True")
            return CreationAllocationStrings.Get("Common.Disabled", "disabled") + " → "
                + CreationAllocationStrings.Get("Common.Enabled", "enabled");
        return CreationFinalizationPage.ChangeLabel(delta);
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
