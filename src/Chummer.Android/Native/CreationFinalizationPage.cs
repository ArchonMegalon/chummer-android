using System.Globalization;
using Chummer.Contracts.Characters;

namespace Chummer.Android.Native;

/// <summary>Collects an explicit dice total; only Core may quote the resulting cash.</summary>
internal sealed class CreationStartingCashPage : NativePageBase
{
    private readonly CharacterCreationFinalizationState _authority;
    private readonly VerticalStackLayout _body = new() { Padding = new Thickness(20, 18, 20, 40), Spacing = 14 };
    private string _roll = string.Empty;
    private IReadOnlyList<string> _blockers = [];
    private long _render, _inputVersion;

    internal CreationStartingCashPage(RunnerSessionCoordinator coordinator, CharacterCreationFinalizationState authority)
        : base(coordinator)
    {
        _authority = authority;
        Title = CreationAllocationStrings.Get("Finalization.StartingCashTitle", "Starting cash");
        AutomationId = "creation-starting-cash-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void OnDisappearing()
    {
        _render++;
        _body.IsEnabled = false;
        base.OnDisappearing();
    }

    protected override void Refresh()
    {
        long render = ++_render, appearance = CaptureAppearanceGeneration();
        _body.Clear();
        _body.IsEnabled = true;
        _body.Add(NativeTheme.Title(Title));
        if (!Coordinator.IsCreationFinalizationStateCurrent(_authority)
            || _authority.StartingCashSource is not { } source)
        {
            _body.Add(NativeTheme.Body(CreationKarmaCopy.Stale, NativeTheme.Danger));
            return;
        }
        _body.Add(NativeTheme.Body(CreationAllocationStrings.Get("Finalization.StartingCashHelp",
            "Enter the starting-cash dice total for the displayed lifestyle. Core applies the carryover limits before adding this cash. Nothing is saved until you confirm the next review."), NativeTheme.Muted));
        var terms = NativeTheme.Body(CreationKarmaCopy.StartingCash(source.Name, source.Dice, source.Multiplier));
        terms.AutomationId = "creation-starting-cash-source";
        _body.Add(terms);
        _body.Add(NativeTheme.Body(source.SourceBook + " · " + source.Page, NativeTheme.Muted));
        _body.Add(NativeTheme.Body(CreationKarmaCopy.DiceTotal));
        var input = new Entry { Text = _roll, Keyboard = Keyboard.Numeric, AutomationId = "creation-starting-cash-roll" };
        var preview = NativeTheme.PrimaryButton(CreationKarmaCopy.PreviewCompletion);
        preview.AutomationId = "creation-starting-cash-preview";
        preview.IsEnabled = TryRoll(out _);
        input.TextChanged += (_, _) =>
        {
            if (!Current()) return;
            _roll = input.Text ?? string.Empty;
            _inputVersion++;
            preview.IsEnabled = TryRoll(out _);
        };
        preview.Clicked += async (_, _) => await RunAsync(async () =>
        {
            if (!Current() || !TryRoll(out int dice)) return;
            long version = _inputVersion;
            _body.IsEnabled = false;
            preview.Text = CreationKarmaCopy.Loading;
            CharacterCreationFinalizationResult<CharacterCreationFinalizationReview> result;
            try
            {
                result = await Coordinator.ReviewCreationFinalizationAsync(_authority.Binding,
                    new CharacterCreationStartingCashChoice(source.AuthorityDigest, dice));
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // A departed page must not show an old asynchronous error or navigate.
                if (!Current() || version != _inputVersion) return;
                throw;
            }
            finally
            {
                if (Current() && version == _inputVersion)
                {
                    _body.IsEnabled = true;
                    preview.Text = CreationKarmaCopy.PreviewCompletion;
                }
            }
            if (!Current() || version != _inputVersion) return;
            _blockers = result.Blockers;
            if (result is { Outcome: CharacterCreationFinalizationOutcomes.Available, Value.CanConfirm: true }
                && Coordinator.IsCreationFinalizationReviewCurrent(result.Value))
                await Navigation.PushAsync(new CreationFinalizationPage(Coordinator, result.Value));
        });
        _body.Add(input);
        _body.Add(preview);
        foreach (string blocker in _blockers)
            _body.Add(NativeTheme.Body(blocker, NativeTheme.Danger));

        bool Current() => render == _render && IsCurrentAppearanceGeneration(appearance)
            && Coordinator.IsCreationFinalizationStateCurrent(_authority);
        bool TryRoll(out int dice) => int.TryParse(_roll, NumberStyles.None, CultureInfo.InvariantCulture, out dice);
    }
}

/// <summary>
/// Read-only projection of Core's sealed whole-build plan.  This surface never
/// edits XML or recomputes rules; it can only submit the exact reviewed plan for
/// one explicit, atomic confirmation.
/// </summary>
public sealed class CreationFinalizationPage : NativePageBase
{
    private readonly CharacterCreationFinalizationReview _review;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };
    private readonly string _idempotencyKey;
    private bool _confirming;
    private long _appearanceGeneration;
    private bool _visible;

    protected override void OnAppearing()
    {
        _appearanceGeneration++;
        _visible = true;
        base.OnAppearing();
    }

    protected override void OnDisappearing()
    {
        _visible = false;
        _appearanceGeneration++;
        base.OnDisappearing();
    }

    public CreationFinalizationPage(
        RunnerSessionCoordinator coordinator,
        CharacterCreationFinalizationReview review) : base(coordinator)
    {
        _review = review ?? throw new ArgumentNullException(nameof(review));
        CharacterCreationFinalizationPlan plan = _review.Plan
            ?? throw new InvalidOperationException("Core did not supply a sealed final creation plan.");
        if (!_review.CanConfirm || _review.Blockers.Count != 0)
            throw new InvalidOperationException("Core did not authorize this final creation review.");

        _idempotencyKey = string.Create(
            CultureInfo.InvariantCulture,
            $"creation-finalization:{review.Binding.WorkspaceId.Value}:"
            + $"{review.Binding.ContentRevision}:{plan.PlanDigest}");
        Title = "Finish creation";
        AutomationId = "creation-finalization-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        if (!Coordinator.IsCreationFinalizationReviewCurrent(_review))
        {
            _body.Add(NativeTheme.Body("This review belongs to a previous runner or account. Reopen the runner and review again.", NativeTheme.Danger));
            return;
        }
        _body.Add(NativeTheme.Eyebrow("Final review"));
        _body.Add(NativeTheme.Title("Enter Career mode"));
        _body.Add(NativeTheme.Body(
            "Review the complete Core-generated delta. Nothing is written until you explicitly confirm; the write is one atomic operation.",
            NativeTheme.Muted));

        Label binding = NativeTheme.Body(
            $"Revision {_review.Binding.ContentRevision} · "
            + $"plan {Short(_review.Plan!.PlanDigest)} · preview {Short(_review.PreviewDigest)}",
            NativeTheme.Muted);
        binding.AutomationId = "creation-finalization-binding";
        _body.Add(NativeAuthoritySemantics.Overlay(
            binding,
            NativeAuthoritySemantics.PositiveRevision(
                "creation-finalization-content-revision",
                _review.Binding.ContentRevision),
            NativeAuthoritySemantics.Digest(
                "creation-finalization-plan-digest",
                _review.Plan.PlanDigest),
            NativeAuthoritySemantics.Digest(
                "creation-finalization-preview-digest",
                _review.PreviewDigest)));

        VerticalStackLayout budget = new() { Spacing = 6 };
        budget.Add(NativeTheme.Eyebrow("After finalization"));
        budget.Add(NativeTheme.Metric("Karma remaining", Number(_review.Plan.KarmaRemaining)));
        budget.Add(NativeTheme.Metric(CreationAllocationStrings.Get("Finalization.StartingCashTitle", "Starting cash"), Number(_review.Plan.StartingNuyen)));
        budget.Add(NativeTheme.Metric("Nuyen remaining", Number(_review.Plan.NuyenRemaining)));
        Border budgetCard = NativeTheme.Card(budget);
        budgetCard.AutomationId = "creation-finalization-costs";
        _body.Add(budgetCard);

        foreach (CharacterCreationFinalizationDelta delta in _review.OrderedDeltas
                     .OrderBy(static item => item.Order))
        {
            VerticalStackLayout card = new() { Spacing = 5 };
            card.Add(NativeTheme.Eyebrow(
                $"{delta.Order.ToString(CultureInfo.InvariantCulture)} · {delta.Kind}"));
            card.Add(NativeTheme.Title(delta.TargetId, 18));
            card.Add(NativeTheme.Body(
                $"{delta.BeforeValue ?? "—"} → {delta.AfterValue ?? "—"}"));
            if (delta.KarmaCost != 0 || delta.NuyenCost != 0)
            {
                card.Add(NativeTheme.Body(
                    $"Karma {Number(delta.KarmaCost)} · Nuyen {Number(delta.NuyenCost)}",
                    NativeTheme.Muted));
            }
            if (delta.SourceAnchorIds.Count > 0)
            {
                card.Add(NativeTheme.Body(
                    string.Join(" · ", delta.SourceAnchorIds),
                    NativeTheme.Muted));
            }
            Border border = NativeTheme.Card(card);
            border.AutomationId = $"creation-finalization-delta-{delta.Order.ToString(CultureInfo.InvariantCulture)}";
            _body.Add(border);
        }

        Label boundary = NativeTheme.Body(
            "Confirming seals this exact revision and plan digest. If any draft changes, Core rejects the command and requires a fresh review.",
            NativeTheme.Muted);
        boundary.AutomationId = "creation-finalization-atomic-boundary";
        _body.Add(NativeTheme.Card(boundary));

        Button confirm = NativeTheme.PrimaryButton(
            _confirming ? "Finalizing…" : "Confirm and enter Career");
        confirm.AutomationId = "creation-finalization-confirm";
        confirm.IsEnabled = !_confirming;
        confirm.Clicked += async (_, _) => await RunAsync(ConfirmAsync);
        _body.Add(confirm);
    }

    private async Task ConfirmAsync()
    {
        if (_confirming || !_visible)
            return;
        long originalAppearance = _appearanceGeneration;
        _confirming = true;
        Refresh();
        try
        {
            CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt> result =
                await Coordinator.ConfirmCreationFinalizationAsync(_review, _idempotencyKey);
            if (!_visible || _appearanceGeneration != originalAppearance)
                return;
            if (result.Value is not { } receipt
                || result.Outcome is not (CharacterCreationFinalizationOutcomes.Applied
                    or CharacterCreationFinalizationOutcomes.Replayed))
            {
                throw new InvalidOperationException(
                    result.Blockers.FirstOrDefault()
                    ?? "Core rejected finalization. Reload and review the current runner revision.");
            }
            if (!Coordinator.CanDisplayCreationFinalizationReceipt(receipt))
                return;
            await Navigation.PushAsync(new CreationFinalizationReceiptPage(
                Coordinator,
                receipt,
                result.Blockers));
        }
        finally
        {
            _confirming = false;
        }
    }

    private static string Short(string value) => value.Length <= 18 ? value : value[..18] + "…";

    private static string Number(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}

public sealed class CreationFinalizationReceiptPage : NativePageBase
{
    private readonly CharacterCreationFinalizationReceipt _receipt;
    private readonly IReadOnlyList<string> _warnings;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };

    public CreationFinalizationReceiptPage(
        RunnerSessionCoordinator coordinator,
        CharacterCreationFinalizationReceipt receipt,
        IReadOnlyList<string> warnings) : base(coordinator)
    {
        _receipt = receipt ?? throw new ArgumentNullException(nameof(receipt));
        _warnings = warnings ?? [];
        Title = "Creation receipt";
        AutomationId = "creation-finalization-receipt-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        if (!Coordinator.CanDisplayCreationFinalizationReceipt(_receipt))
        {
            _body.Add(NativeTheme.Body("Return to the original account to view this receipt.", NativeTheme.Danger));
            return;
        }
        _body.Add(NativeTheme.Eyebrow("Durable receipt"));
        bool reopened = Coordinator.IsCreationFinalizationReceiptCurrent(_receipt);
        _body.Add(NativeTheme.Title(reopened ? "Career mode is ready" : "Creation was saved"));

        VerticalStackLayout receipt = new() { Spacing = 6 };
        receipt.Add(NativeTheme.Metric("Receipt", Short(_receipt.ReceiptDigest)));
        receipt.Add(NativeTheme.Metric("Plan", Short(_receipt.PlanDigest)));
        receipt.Add(NativeTheme.Metric("Revision", _receipt.ContentRevision.ToString(CultureInfo.InvariantCulture)));
        receipt.Add(NativeTheme.Metric("Build method", _receipt.BuildMethod));
        receipt.Add(NativeTheme.Metric("Created", _receipt.CharacterCreated ? "Yes" : "No"));
        Border card = NativeTheme.Card(receipt);
        card.AutomationId = "creation-finalization-receipt";
        _body.Add(NativeAuthoritySemantics.Overlay(
            card,
            NativeAuthoritySemantics.PositiveRevision(
                "creation-finalization-receipt-previous-content-revision",
                _receipt.PreviousContentRevision),
            NativeAuthoritySemantics.PositiveRevision(
                "creation-finalization-receipt-content-revision",
                _receipt.ContentRevision),
            NativeAuthoritySemantics.PositiveRevision(
                "creation-finalization-receipt-saved-revision",
                _receipt.SavedRevision),
            NativeAuthoritySemantics.Identifier(
                "creation-finalization-receipt-build-method",
                _receipt.BuildMethod),
            NativeAuthoritySemantics.Digest(
                "creation-finalization-receipt-plan-digest",
                _receipt.PlanDigest),
            NativeAuthoritySemantics.Digest(
                "creation-finalization-receipt-preview-digest",
                _receipt.PreviewDigest),
            NativeAuthoritySemantics.Digest(
                "creation-finalization-receipt-digest",
                _receipt.ReceiptDigest)));

        Label reopen = NativeTheme.Body(
            reopened
                ? "Fresh reopen verified: this runner is now using Career mode."
                : "The atomic receipt is durable, but the Career view must be reopened before further edits.",
            reopened ? NativeTheme.Success : NativeTheme.Danger);
        reopen.AutomationId = "creation-finalization-career-reopen";
        _body.Add(reopen);

        for (int index = 0; index < _warnings.Count; index++)
        {
            Label warningLabel = NativeTheme.Body(_warnings[index], NativeTheme.Danger);
            warningLabel.AutomationId =
                $"creation-finalization-receipt-warning-{index + 1}";
            _body.Add(warningLabel);
        }

        Button done = NativeTheme.PrimaryButton("Open Career runner");
        done.AutomationId = "creation-finalization-open-career";
        done.IsEnabled = reopened;
        done.Clicked += async (_, _) =>
        {
            if (Coordinator.IsCreationFinalizationReceiptCurrent(_receipt))
                await Navigation.PopToRootAsync();
        };
        _body.Add(done);
    }

    private static string Short(string value) => value.Length <= 18 ? value : value[..18] + "…";
}
