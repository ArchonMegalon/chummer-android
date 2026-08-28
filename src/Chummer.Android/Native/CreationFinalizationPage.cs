using System.Globalization;
using Chummer.Contracts.Characters;

namespace Chummer.Android.Native;

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
        budget.Add(NativeTheme.Metric("Starting nuyen", Number(_review.Plan.StartingNuyen)));
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
        if (_confirming)
            return;
        _confirming = true;
        Refresh();
        try
        {
            CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt> result =
                await Coordinator.ConfirmCreationFinalizationAsync(_review, _idempotencyKey);
            if (result.Value is not { } receipt
                || result.Outcome is not (CharacterCreationFinalizationOutcomes.Applied
                    or CharacterCreationFinalizationOutcomes.Replayed))
            {
                throw new InvalidOperationException(
                    result.Blockers.FirstOrDefault()
                    ?? "Core rejected finalization. Reload and review the current runner revision.");
            }
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
        _body.Add(NativeTheme.Eyebrow("Durable receipt"));
        _body.Add(NativeTheme.Title("Career mode is ready"));

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
            Coordinator.State.Profile?.Created == true
                && Coordinator.State.WorkspaceId == _receipt.WorkspaceId
                && Coordinator.State.ContentRevision == _receipt.ContentRevision
                ? "Fresh reopen verified: this runner is now using Career mode."
                : "The atomic receipt is durable, but the Career view must be reopened before further edits.",
            Coordinator.State.Profile?.Created == true ? NativeTheme.Success : NativeTheme.Danger);
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
        done.Clicked += async (_, _) => await Navigation.PopToRootAsync();
        _body.Add(done);
    }

    private static string Short(string value) => value.Length <= 18 ? value : value[..18] + "…";
}
