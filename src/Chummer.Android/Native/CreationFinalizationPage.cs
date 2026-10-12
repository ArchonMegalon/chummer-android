using System.Globalization;
using Chummer.Contracts.Characters;

namespace Chummer.Android.Native;

/// <summary>Collects an explicit dice total; only Core may quote the resulting cash.</summary>
internal sealed class CreationStartingCashPage : NativePageBase
{
    private readonly CharacterCreationFinalizationState _authority;
    private readonly VerticalStackLayout _body = new() { Padding = new Thickness(20, 18, 20, 40), Spacing = 14 };
    private string _roll = string.Empty;
    private string? _sourceDigest;
    private IReadOnlyList<string> _blockers = [];
    private long _render, _inputVersion;

    internal CreationStartingCashPage(RunnerSessionCoordinator coordinator, CharacterCreationFinalizationState authority)
        : base(coordinator)
    {
        _authority = authority;
        _sourceDigest = authority.StartingCashSource?.AuthorityDigest;
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
        var sources = _authority.StartingCashSources?.ToArray()
            ?? (_authority.StartingCashSource is { } fallback ? [fallback] : []);
        if (!Coordinator.IsCreationFinalizationStateCurrent(_authority)
            || sources.FirstOrDefault(option => option.AuthorityDigest == _sourceDigest) is not { } source)
        {
            _body.Add(NativeTheme.Body(CreationKarmaCopy.Stale, NativeTheme.Danger));
            return;
        }
        _body.Add(NativeTheme.Body(CreationAllocationStrings.Get("Finalization.StartingCashHelp",
            "Enter the starting-cash dice total for the displayed lifestyle. Core applies the carryover limits before adding this cash. Nothing is saved until you confirm the next review."), NativeTheme.Muted));
        if (sources.Length > 1)
        {
            var picker = new Picker
            {
                Title = CreationAllocationStrings.Get("Finalization.StartingLifestyle", "Lifestyle for starting cash"),
                TextColor = NativeTheme.Text,
                BackgroundColor = NativeTheme.Surface,
                AutomationId = "creation-starting-cash-lifestyle",
                ItemsSource = sources.Select(option => CreationKarmaCopy.StartingCash(
                    option.Name, option.Dice, option.Multiplier)).ToArray(),
                SelectedIndex = Array.FindIndex(sources, option => option.AuthorityDigest == _sourceDigest)
            };
            picker.SelectedIndexChanged += (_, _) =>
            {
                if (!Current() || picker.SelectedIndex < 0 || picker.SelectedIndex >= sources.Length) return;
                string selected = sources[picker.SelectedIndex].AuthorityDigest;
                if (selected == _sourceDigest) return;
                _sourceDigest = selected;
                _roll = string.Empty;
                _blockers = [];
                _inputVersion++;
                Refresh();
            };
            _body.Add(picker);
        }
        var terms = NativeTheme.Body(CreationKarmaCopy.StartingCash(source.Name, source.Dice, source.Multiplier));
        terms.AutomationId = "creation-starting-cash-source";
        _body.Add(terms);
        _body.Add(NativeTheme.Body(source.SourceBook + " · " + source.Page, NativeTheme.Muted));
        _body.Add(NativeTheme.Body(CreationKarmaCopy.DiceTotal));
        _body.Add(NativeTheme.Body(CreationKarmaCopy.DiceTotalHelp, NativeTheme.Muted));
        var input = new Entry
        {
            Text = _roll,
            Keyboard = Keyboard.Numeric,
            TextColor = NativeTheme.Text,
            BackgroundColor = NativeTheme.Surface,
            AutomationId = "creation-starting-cash-roll"
        };
        var preview = NativeTheme.PrimaryButton(CreationKarmaCopy.PreviewCompletion);
        preview.AutomationId = "creation-starting-cash-preview";
        preview.IsEnabled = TryRoll(out _);
        List<Label> reviewFeedback = [];
        input.TextChanged += (_, _) =>
        {
            if (!Current()) return;
            _roll = input.Text ?? string.Empty;
            _inputVersion++;
            // The last rejection describes the old input. Retire only that
            // feedback: keep focus and require a fresh Core review to proceed.
            _blockers = [];
            foreach (Label feedback in reviewFeedback) feedback.IsVisible = false;
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
            else
                Refresh();
        });
        _body.Add(input);
        _body.Add(preview);
        foreach (string blocker in _blockers)
        {
            // Core also requests a choice when it rejects the supplied total.
            // That is the same correction, not a second user-facing error.
            if (blocker == CharacterCreationFinalizationBlockers.StartingCashChoiceRequired
                && _blockers.Contains(CharacterCreationFinalizationBlockers.StartingCashChoiceInvalid))
                continue;
            Label feedback = NativeTheme.Body(
                blocker == CharacterCreationFinalizationBlockers.StartingCashChoiceInvalid
                    ? CreationAllocationStrings.Get("Finalization.InvalidDiceTotal",
                        "That total does not match these dice. Check your roll and try again.")
                    : blocker == CharacterCreationFinalizationBlockers.StartingCashChoiceRequired
                        ? CreationKarmaCopy.DiceTotal : blocker,
                NativeTheme.Danger);
            reviewFeedback.Add(feedback);
            _body.Add(feedback);
        }

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
    private CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt>? _completion;
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
        Title = Copy("Title", "Finish creation");
        AutomationId = "creation-finalization-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        if (_completion?.Value is { } saved)
        {
            // A durable completion survives navigation/reload failures. Never
            // offer Confirm again, even if this page disappears and returns.
            if (!Coordinator.CanDisplayCreationFinalizationReceipt(saved))
            {
                _body.Add(NativeTheme.Body(Copy("ReceiptAccount", "Return to the original account to view this confirmation."), NativeTheme.Danger));
                return;
            }
            _body.Add(NativeTheme.Title(Copy("SavedTitle", "Creation saved")));
            bool ready = _completion.Blockers.Count == 0 && Coordinator.IsCreationFinalizationReceiptCurrent(saved);
            _body.Add(NativeTheme.Body(ready
                ? Copy("Reopened", "Your saved runner has reopened in Career mode.")
                : Copy("ReopenRequired", "Your changes are saved. Reopen your runner before continuing in Career mode.")));
            Button continueSaved = NativeTheme.PrimaryButton(ready
                ? Copy("OpenCareer", "Open Career runner") : Copy("ViewConfirmation", "View saved confirmation"));
            continueSaved.AutomationId = "creation-finalization-continue-saved";
            continueSaved.IsEnabled = !_confirming;
            long appearance = _appearanceGeneration;
            continueSaved.Clicked += async (_, _) =>
            {
                if (!IsCurrentPage(appearance) || !ReferenceEquals(continueSaved.Parent, _body)) return;
                await RunAsync(() => OpenCompletionAsync(appearance, Navigation.NavigationStack.ToArray()));
            };
            _body.Add(continueSaved);
            return;
        }
        if (!Coordinator.IsCreationFinalizationReviewCurrent(_review))
        {
            _body.Add(NativeTheme.Body(Copy("StaleReview", "This review is no longer current. Reopen your runner and review again."), NativeTheme.Danger));
            return;
        }
        _body.Add(NativeTheme.Eyebrow(Copy("Review", "Final review")));
        _body.Add(NativeTheme.Title(Copy("EnterCareer", "Enter Career mode")));
        Label introduction = NativeTheme.Body(Copy("ReviewHelp",
            "Check the changes and remaining funds below. They are saved together only when you confirm."), NativeTheme.Muted);

        Label binding = NativeTheme.Body(
            $"Revision {_review.Binding.ContentRevision} · "
            + $"plan {Short(_review.Plan!.PlanDigest)} · preview {Short(_review.PreviewDigest)}",
            NativeTheme.Muted);
        binding.AutomationId = "creation-finalization-binding";
        binding.IsVisible = false;
        _body.Add(NativeAuthoritySemantics.Overlay(
            introduction,
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
        budget.Add(NativeTheme.Eyebrow(Copy("After", "After finalization")));
        budget.Add(NativeTheme.Metric(Copy("KarmaRemaining", "Karma remaining"), Number(_review.Plan.KarmaRemaining)));
        budget.Add(NativeTheme.Metric(CreationAllocationStrings.Get("Finalization.StartingCashTitle", "Starting cash"), Number(_review.Plan.StartingNuyen)));
        budget.Add(NativeTheme.Metric(Copy("NuyenRemaining", "Nuyen remaining"), Number(_review.Plan.NuyenRemaining)));
        Border budgetCard = NativeTheme.Card(budget);
        budgetCard.AutomationId = "creation-finalization-costs";
        _body.Add(budgetCard);

        // Keep every reviewed change and cost visible. Raw source anchors are
        // diagnostics, not extra choices; they can span many phone screens.
        List<Label> sourceDetails = [binding];
        bool showDetails = false;
        Button details = NativeTheme.SecondaryButton(CreationFlowStrings.Get(
            "Qualities.ShowDetails", "Show technical details"));
        details.AutomationId = "creation-finalization-technical-details-toggle";
        details.Clicked += (_, _) =>
        {
            if (!_visible || _confirming || !ReferenceEquals(details.Parent, _body)
                || !Coordinator.IsCreationFinalizationReviewCurrent(_review))
                return;
            showDetails = !showDetails;
            foreach (Label source in sourceDetails)
                source.IsVisible = showDetails;
            details.Text = showDetails
                ? CreationFlowStrings.Get("Qualities.HideDetails", "Hide technical details")
                : CreationFlowStrings.Get("Qualities.ShowDetails", "Show technical details");
        };
        _body.Add(details);
        _body.Add(binding);

        foreach (CharacterCreationFinalizationDelta delta in _review.OrderedDeltas
                     .OrderBy(static item => item.Order))
        {
            VerticalStackLayout card = new() { Spacing = 5 };
            card.Add(NativeTheme.Eyebrow(CreationAllocationStrings.Format(
                "Finalization.Change", "Change {0}", delta.Order)));
            Label target = NativeTheme.Title(TargetLabel(delta), 18);
            target.AutomationId = $"creation-finalization-target-{delta.Order.ToString(CultureInfo.InvariantCulture)}";
            card.Add(target);
            card.Add(NativeTheme.Body(ChangeLabel(delta)));
            if (delta.KarmaCost != 0 || delta.NuyenCost != 0)
            {
                card.Add(NativeTheme.Body(
                    $"Karma {Number(delta.KarmaCost)} · Nuyen {Number(delta.NuyenCost)}",
                    NativeTheme.Muted));
            }
            Label source = NativeTheme.Body(
                delta.TargetId + $"\n{delta.Kind} · {delta.BeforeValue ?? "—"} → {delta.AfterValue ?? "—"}"
                + (delta.SourceAnchorIds.Count > 0
                    ? "\n" + string.Join(" · ", delta.SourceAnchorIds) : string.Empty),
                NativeTheme.Muted);
            source.AutomationId = $"creation-finalization-source-{delta.Order.ToString(CultureInfo.InvariantCulture)}";
            source.IsVisible = false;
            sourceDetails.Add(source);
            card.Add(source);
            Border border = NativeTheme.Card(card);
            border.AutomationId = $"creation-finalization-delta-{delta.Order.ToString(CultureInfo.InvariantCulture)}";
            _body.Add(border);
        }

        Label boundary = NativeTheme.Body(
            Copy("ConfirmHelp", "Confirm to finish creation and start Career mode. If your runner changes before saving, you will need to review again."),
            NativeTheme.Muted);
        boundary.AutomationId = "creation-finalization-atomic-boundary";
        _body.Add(NativeTheme.Card(boundary));

        Button confirm = NativeTheme.PrimaryButton(
            _confirming ? Copy("Saving", "Saving…") : Copy("Confirm", "Confirm and enter Career"));
        confirm.AutomationId = "creation-finalization-confirm";
        confirm.IsEnabled = !_confirming;
        long confirmAppearance = _appearanceGeneration;
        confirm.Clicked += async (_, _) =>
        {
            if (!IsCurrentPage(confirmAppearance) || !ReferenceEquals(confirm.Parent, _body)) return;
            await RunAsync(ConfirmAsync);
        };
        _body.Add(confirm);
    }

    private async Task ConfirmAsync()
    {
        if (_confirming || _completion is not null || !IsCurrentPage(_appearanceGeneration))
            return;
        long originalAppearance = _appearanceGeneration;
        Page[] originalStack = Navigation.NavigationStack.ToArray();
        _confirming = true;
        Refresh();
        try
        {
            CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt> result =
                await Coordinator.ConfirmCreationFinalizationAsync(_review, _idempotencyKey);
            // Retain a known commit before inspecting the current appearance.
            // Returning to this page must only continue/read, never re-submit.
            if (result.Value is not null && result.Outcome is (CharacterCreationFinalizationOutcomes.Applied
                or CharacterCreationFinalizationOutcomes.Replayed)) _completion = result;
            if (!IsCurrentPage(originalAppearance) || !Navigation.NavigationStack.SequenceEqual(originalStack)) return;
            if (result.Value is null
                || result.Outcome is not (CharacterCreationFinalizationOutcomes.Applied
                    or CharacterCreationFinalizationOutcomes.Replayed))
            {
                throw new InvalidOperationException(
                    result.Blockers.FirstOrDefault()
                    ?? "Core rejected finalization. Reload and review the current runner revision.");
            }
            await OpenCompletionAsync(originalAppearance, originalStack);
        }
        finally
        {
            _confirming = false;
            // A user may return while the durable commit is finishing. Expose
            // only the retained read-only continuation on that current page;
            // do not auto-navigate from the retired appearance's completion.
            if (_completion is not null && IsCurrentPage(_appearanceGeneration)) Refresh();
        }
    }

    private bool IsCurrentPage(long appearance) => _visible && _appearanceGeneration == appearance
        && ReferenceEquals(Navigation.NavigationStack.LastOrDefault(), this);

    private async Task OpenCompletionAsync(long appearance, IReadOnlyList<Page> originalStack)
    {
        var navigation = Navigation;
        bool Current() => IsCurrentPage(appearance) && navigation.NavigationStack.SequenceEqual(originalStack);
        if (!Current() || _completion?.Value is not { } receipt
            || !Coordinator.CanDisplayCreationFinalizationReceipt(receipt)) return;
        if (_completion.Blockers.Count == 0 && Coordinator.IsCreationFinalizationReceiptCurrent(receipt))
        {
            try
            {
                // The coordinator has already restored the exact durable Career
                // revision. Retire the wizard, including its consumed reviews.
                await navigation.PopToRootAsync(animated: false);
                return;
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                // A navigation failure cannot turn a known commit into a retry.
                // If this same screen still owns navigation, retain its receipt.
            }
        }
        if (!Current() || !Coordinator.CanDisplayCreationFinalizationReceipt(receipt)) return;
        await navigation.PushAsync(new CreationFinalizationReceiptPage(Coordinator, receipt, _completion.Blockers));
    }

    // Display only: the exact Core-supplied name is already bound into the plan.
    // Never resolve a similarly named catalog row or change the typed identity.
    // Older receipts without this optional field retain an explicit ID fallback.
    internal static string TargetLabel(CharacterCreationFinalizationDelta delta) =>
        IsCareerTransition(delta) ? Copy("Mode", "Mode")
            : !string.IsNullOrWhiteSpace(delta.TargetName) ? delta.TargetName
            : delta.Kind == CharacterCreationFinalizationDeltaKinds.Attribute
                ? CreationAllocationStrings.AttributeName(delta.TargetId)
            : delta.Kind == CharacterCreationFinalizationDeltaKinds.Resources ? delta.TargetId switch
            {
                "nuyen" => "Nuyen",
                "karma" => "Karma",
                "lifestyle" => Copy("CashLifestyle", "Starting-cash lifestyle"),
                "starting-cash-dice" => Copy("CashDiceTotal", "Starting-cash dice total"),
                _ => delta.TargetId
            } : delta.TargetId;

    internal static string ChangeLabel(CharacterCreationFinalizationDelta delta) =>
        IsCareerTransition(delta) ? Copy("CareerTransition", "Creation → Career")
            : $"{ValueLabel(delta, delta.BeforeValue)} → {ValueLabel(delta, delta.AfterValue)}";

    private static bool IsCareerTransition(CharacterCreationFinalizationDelta delta) =>
        delta.Kind == CharacterCreationFinalizationDeltaKinds.Lifecycle && delta.TargetId == "created"
        && delta.BeforeValue == "False" && delta.AfterValue == "True";

    private static string ValueLabel(CharacterCreationFinalizationDelta delta, string? value) =>
        delta.Kind == CharacterCreationFinalizationDeltaKinds.Skill && value == "native"
            ? CreationKarmaCopy.NativeLanguage : value ?? "—";

    private static string Copy(string key, string fallback) => CreationAllocationStrings.Get("Finalization." + key, fallback);

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
        Title = Copy("SavedTitle", "Creation saved");
        AutomationId = "creation-finalization-receipt-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        if (!Coordinator.CanDisplayCreationFinalizationReceipt(_receipt))
        {
            _body.Add(NativeTheme.Body(Copy("ReceiptAccount", "Return to the original account to view this confirmation."), NativeTheme.Danger));
            return;
        }
        _body.Add(NativeTheme.Eyebrow(Copy("SavedTitle", "Creation saved")));
        bool reopened = Coordinator.IsCreationFinalizationReceiptCurrent(_receipt);
        _body.Add(NativeTheme.Title(reopened ? Copy("CareerReady", "Career mode is ready") : Copy("SavedTitle", "Creation saved")));

        VerticalStackLayout receipt = new() { Spacing = 6 };
        receipt.Add(NativeTheme.Metric(Copy("BuildMethod", "Build method"), BuildMethodLabel(_receipt.BuildMethod)));
        receipt.Add(NativeTheme.Metric(Copy("Mode", "Mode"), _receipt.CharacterCreated
            ? Copy("Career", "Career") : Copy("Creation", "Creation")));
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
                ? Copy("Reopened", "Your saved runner has reopened in Career mode.")
                : Copy("ReopenRequired", "Your changes are saved. Reopen your runner before continuing in Career mode."),
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

        Button done = NativeTheme.PrimaryButton(Copy("OpenCareer", "Open Career runner"));
        done.AutomationId = "creation-finalization-open-career";
        done.IsEnabled = reopened;
        done.Clicked += async (_, _) =>
        {
            if (Coordinator.IsCreationFinalizationReceiptCurrent(_receipt))
                await Navigation.PopToRootAsync();
        };
        _body.Add(done);

        VerticalStackLayout technical = new() { Spacing = 6, IsVisible = false,
            AutomationId = "creation-finalization-receipt-technical-details" };
        technical.Add(NativeTheme.Metric("Receipt", Short(_receipt.ReceiptDigest)));
        technical.Add(NativeTheme.Metric("Plan", Short(_receipt.PlanDigest)));
        technical.Add(NativeTheme.Metric("Revision", _receipt.ContentRevision.ToString(CultureInfo.InvariantCulture)));
        Button details = NativeTheme.SecondaryButton(CreationFlowStrings.Get("Qualities.ShowDetails", "Show technical details"));
        details.AutomationId = "creation-finalization-receipt-technical-details-toggle";
        long appearance = CaptureAppearanceGeneration();
        details.Clicked += (_, _) =>
        {
            if (!IsCurrentAppearanceGeneration(appearance) || !ReferenceEquals(details.Parent, _body)
                || !Coordinator.CanDisplayCreationFinalizationReceipt(_receipt))
                return;
            technical.IsVisible = !technical.IsVisible;
            details.Text = technical.IsVisible
                ? CreationFlowStrings.Get("Qualities.HideDetails", "Hide technical details")
                : CreationFlowStrings.Get("Qualities.ShowDetails", "Show technical details");
        };
        _body.Add(details);
        _body.Add(technical);
    }

    internal static string BuildMethodLabel(string method) => method switch
    {
        CharacterCreationBuildMethods.Priority => Copy("Method.Priority", "Priority"),
        CharacterCreationBuildMethods.SumToTen => Copy("Method.SumToTen", "Sum-to-Ten"),
        CharacterCreationBuildMethods.Karma => Copy("Method.Karma", "Karma"),
        CharacterCreationBuildMethods.LifeModules => Copy("Method.LifeModules", "Life Modules"),
        _ => method
    };

    private static string Copy(string key, string fallback) => CreationAllocationStrings.Get("Finalization." + key, fallback);

    private static string Short(string value) => value.Length <= 18 ? value : value[..18] + "…";
}
