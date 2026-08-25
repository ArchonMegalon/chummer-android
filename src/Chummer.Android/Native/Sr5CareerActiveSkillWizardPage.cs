using System.Globalization;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class Sr5CareerActiveSkillWizardPage : NativePageBase
{
    private readonly CareerActiveSkillAdvanceEditorState _editor;
    private readonly Picker _skills;
    private readonly Label _rating;
    private readonly Label _cost;
    private readonly Label _blocker;
    private readonly Label _recovery;
    private readonly Button _review;
    private readonly Button _resume;
    private readonly Button _discardCheckpoint;
    private CharacterCareerActiveSkillAdvanceQuote? _selected;
    private Sr5CareerActiveSkillDraft? _recoveryDraft;
    private Sr5CareerDraftCheckpoint? _checkpoint;

    public Sr5CareerActiveSkillWizardPage(
        RunnerSessionCoordinator coordinator,
        CareerActiveSkillAdvanceEditorState editor) : base(coordinator)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _selected = editor.Skills.FirstOrDefault();
        Title = "Advance skill";
        AutomationId = Sr5CareerWizardRoutes.ActiveSkillChoose;

        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow("SR5 Career · 1 of 3"));
        body.Add(NativeTheme.Title("Choose an active skill"));
        body.Add(NativeTheme.Body(
            "Only exact saved skills from this revision are shown. Core owns source resolution, rating maximum, Karma modifiers and expense undo semantics.",
            NativeTheme.Muted));

        body.Add(NativeTheme.FieldLabel("Active skill"));
        _skills = new Picker
        {
            AutomationId = "sr5-career-active-skill-picker",
            Title = "Saved active skill",
            ItemsSource = editor.Skills.Select(SkillLabel).ToArray(),
            SelectedIndex = editor.Skills.Count > 0 ? 0 : -1,
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        _skills.SelectedIndexChanged += (_, _) => SelectSkill();
        body.Add(_skills);

        _rating = NativeTheme.Body(string.Empty, NativeTheme.Text);
        _rating.AutomationId = "sr5-career-active-skill-rating";
        body.Add(_rating);
        _cost = NativeTheme.Body(string.Empty, NativeTheme.Text);
        _cost.AutomationId = "sr5-career-active-skill-cost";
        body.Add(_cost);
        _blocker = NativeTheme.Body(string.Empty, NativeTheme.Danger);
        _blocker.AutomationId = "sr5-career-active-skill-blocker";
        body.Add(_blocker);

        _recovery = NativeTheme.Body(string.Empty, NativeTheme.Muted);
        _recovery.AutomationId = "sr5-career-active-skill-recovery";
        body.Add(_recovery);

        if (editor.OmittedSkillCount > 0)
        {
            Label omitted = NativeTheme.Body(
                $"{editor.OmittedSkillCount.ToString(CultureInfo.InvariantCulture)} skill(s) are omitted because their exact source, rating Improvement or custom compensation authority cannot be reproduced safely.",
                NativeTheme.Danger);
            omitted.AutomationId = "sr5-career-active-skill-omitted";
            body.Add(NativeTheme.Card(omitted));
        }

        _review = NativeTheme.PrimaryButton("Review advancement");
        _review.AutomationId = "sr5-career-active-skill-review";
        _review.Clicked += async (_, _) => await RunAsync(OpenReviewAsync);
        body.Add(_review);

        _resume = NativeTheme.SecondaryButton("Resume reviewed advancement");
        _resume.AutomationId = "sr5-career-active-skill-resume";
        _resume.Clicked += async (_, _) => await RunAsync(ResumeReviewAsync);
        body.Add(_resume);

        _discardCheckpoint = NativeTheme.SecondaryButton("Discard checkpoint after ledger check");
        _discardCheckpoint.AutomationId = "sr5-career-active-skill-discard-checkpoint";
        _discardCheckpoint.Clicked += async (_, _) => await RunAsync(DiscardCheckpointAsync);
        body.Add(_discardCheckpoint);
        Content = new ScrollView { Content = body };
        LoadRecoveryCheckpoint();
        RefreshEnabledState();
    }

    protected override void Refresh() => RefreshEnabledState();

    private static string SkillLabel(CharacterCareerActiveSkillAdvanceQuote skill)
        => $"{skill.Name} · {skill.TotalBaseRating.ToString(CultureInfo.InvariantCulture)} → "
           + $"{(skill.TotalBaseRating + 1).ToString(CultureInfo.InvariantCulture)} · "
           + $"{skill.KarmaCost.ToString(CultureInfo.InvariantCulture)} Karma";

    private void SelectSkill()
    {
        _selected = _skills.SelectedIndex >= 0 && _skills.SelectedIndex < _editor.Skills.Count
            ? _editor.Skills[_skills.SelectedIndex]
            : null;
        RefreshEnabledState();
    }

    private void RefreshEnabledState()
    {
        bool revisionMatches = Coordinator.State.WorkspaceId == _editor.WorkspaceId
            && Coordinator.State.ContentRevision == _editor.ContentRevision;
        _skills.IsEnabled = revisionMatches && _editor.Skills.Count > 0;
        _rating.Text = _selected is null
            ? "No exact active-skill quote is available."
            : $"Current {_selected.TotalBaseRating.ToString(CultureInfo.InvariantCulture)} · "
              + $"after {_selected.TotalBaseRating + 1} · maximum {_selected.RatingMaximum.ToString(CultureInfo.InvariantCulture)}";
        _cost.Text = _selected is null
            ? string.Empty
            : $"Cost {_selected.KarmaCost.ToString(CultureInfo.InvariantCulture)} Karma · "
              + $"available {_selected.AvailableKarma.ToString(CultureInfo.InvariantCulture)} · "
              + $"after {_selected.AvailableKarma - _selected.KarmaCost}";
        _blocker.Text = !revisionMatches
            ? "This runner changed. Discard this draft and reopen advancement."
            : _selected?.Blocker switch
            {
                CharacterCareerActiveSkillAdvanceBlocker.AtMaximum =>
                    "This skill is already at its exact career maximum.",
                CharacterCareerActiveSkillAdvanceBlocker.InsufficientKarma =>
                    "The runner does not have enough Karma for this advancement.",
                _ => string.Empty
            };
        _review.IsEnabled = revisionMatches
            && _checkpoint is null
            && _selected is { CanAdvance: true }
            && CharacterCareerActiveSkillAdvanceRules.IsCoherent(_selected);
        _resume.IsVisible = _recoveryDraft is not null;
        _resume.IsEnabled = revisionMatches && _recoveryDraft is not null;
        _discardCheckpoint.IsVisible = _checkpoint is not null;
        _discardCheckpoint.IsEnabled = _checkpoint is not null;
    }

    private async Task OpenReviewAsync()
    {
        if (!Sr5CareerActiveSkillDraft.TryCreate(
                _editor,
                _selected,
                Guid.NewGuid(),
                DateTime.Now,
                out Sr5CareerActiveSkillDraft draft,
                out string blocker))
        {
            await DisplayAlertAsync("Cannot review", blocker, "OK");
            return;
        }
        if (!draft.Matches(Coordinator.State.WorkspaceId, Coordinator.State.ContentRevision))
        {
            await DisplayAlertAsync(
                "Runner changed",
                "Discard this draft and reopen active-skill advancement.",
                "OK");
            return;
        }
        if (!Sr5CareerDraftCheckpointStore.TrySave(
                Sr5CareerDraftCheckpoint.FromDraft(draft),
                out blocker))
        {
            await DisplayAlertAsync("Review not checkpointed", blocker, "OK");
            return;
        }

        await Navigation.PushAsync(new Sr5CareerActiveSkillReviewPage(Coordinator, draft));
    }

    private async Task ResumeReviewAsync()
    {
        if (_recoveryDraft is null
            || !_recoveryDraft.Matches(Coordinator.State.WorkspaceId, Coordinator.State.ContentRevision))
        {
            await DisplayAlertAsync(
                "Draft cannot resume",
                "The saved review no longer matches this exact runner revision.",
                "OK");
            return;
        }
        await Navigation.PushAsync(new Sr5CareerActiveSkillReviewPage(Coordinator, _recoveryDraft));
    }

    private async Task DiscardCheckpointAsync()
    {
        if (_checkpoint is null)
        {
            return;
        }
        bool confirmed = await DisplayAlertAsync(
            "Discard saved review?",
            _checkpoint.Phase == Sr5CareerCheckpointPhase.Applying
                ? "First inspect the saved runner and expense ledger. This checkpoint marks an uncertain one-shot apply and must not be retried blindly. Discard it now?"
                : "This removes the saved reviewed draft without changing the runner. Discard it?",
            "Discard checkpoint",
            "Keep");
        if (!confirmed)
        {
            return;
        }
        if (!Sr5CareerDraftCheckpointStore.TryClear(_checkpoint.IdempotencyKey, out string blocker))
        {
            await DisplayAlertAsync("Checkpoint not cleared", blocker, "OK");
            return;
        }

        _checkpoint = null;
        _recoveryDraft = null;
        _recovery.Text = string.Empty;
        RefreshEnabledState();
    }

    private void LoadRecoveryCheckpoint()
    {
        if (!Sr5CareerDraftCheckpointStore.TryLoad(
                out Sr5CareerDraftCheckpoint checkpoint,
                out string loadBlocker))
        {
            _recovery.Text = loadBlocker;
            _recovery.TextColor = string.IsNullOrWhiteSpace(loadBlocker)
                ? NativeTheme.Muted
                : NativeTheme.Danger;
            return;
        }
        _checkpoint = checkpoint;

        if (checkpoint.TryResume(_editor, out Sr5CareerActiveSkillDraft draft, out string blocker))
        {
            _recoveryDraft = draft;
            _recovery.Text = "A reviewed advancement survived process restart and can be resumed with the same action identity.";
            _recovery.TextColor = NativeTheme.Muted;
            int selectedIndex = _editor.Skills
                .Select((candidate, index) => (candidate, index))
                .First(pair => pair.candidate.Identity == draft.Quote.Identity)
                .index;
            _skills.SelectedIndex = selectedIndex;
            return;
        }

        _recovery.Text = blocker;
        _recovery.TextColor = NativeTheme.Danger;
    }
}

public sealed class Sr5CareerActiveSkillReviewPage : NativePageBase
{
    private readonly Sr5CareerActiveSkillDraft _draft;
    private readonly Button _apply;
    private readonly Label _blocker;
    private int _attempted;

    public Sr5CareerActiveSkillReviewPage(
        RunnerSessionCoordinator coordinator,
        Sr5CareerActiveSkillDraft draft) : base(coordinator)
    {
        _draft = draft ?? throw new ArgumentNullException(nameof(draft));
        Title = "Review advancement";
        AutomationId = Sr5CareerWizardRoutes.ActiveSkillReview;

        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow("SR5 Career · 2 of 3"));
        body.Add(NativeTheme.Title("Review exact diff"));

        VerticalStackLayout diff = new() { Spacing = 8 };
        diff.Add(NativeTheme.Metric("Skill", _draft.Quote.Name));
        diff.Add(NativeTheme.Metric(
            "Rating",
            $"{_draft.Quote.TotalBaseRating.ToString(CultureInfo.InvariantCulture)} → "
            + $"{(_draft.Quote.TotalBaseRating + 1).ToString(CultureInfo.InvariantCulture)}"));
        diff.Add(NativeTheme.Metric(
            "Karma",
            $"{_draft.Quote.AvailableKarma.ToString(CultureInfo.InvariantCulture)} → "
            + $"{_draft.Plan.SavedCharacterKarma.ToString(CultureInfo.InvariantCulture)} "
            + $"(cost {_draft.Quote.KarmaCost.ToString(CultureInfo.InvariantCulture)})"));
        diff.Add(NativeTheme.Metric("Expense", _draft.Plan.ExpenseReason));
        diff.Add(NativeTheme.Metric("Undo type", _draft.Plan.KarmaUndoType));
        diff.Add(NativeTheme.Metric("Skill identity", _draft.Quote.Identity.SkillId.ToString("D")));
        diff.Add(NativeTheme.Metric("Expense identity", _draft.Plan.ExpenseId.ToString("D")));
        body.Add(NativeTheme.Card(diff));

        Label binding = NativeTheme.Body(
            $"Expected revision {_draft.ExpectedContentRevision.ToString(CultureInfo.InvariantCulture)} · "
            + $"rule {ShortDigest(_draft.Quote.RuleDigest)} · source {ShortDigest(_draft.Quote.SourceRevision)}",
            NativeTheme.Muted);
        binding.AutomationId = "sr5-career-active-skill-review-binding";
        body.Add(binding);

        _blocker = NativeTheme.Body(string.Empty, NativeTheme.Danger);
        _blocker.AutomationId = "sr5-career-active-skill-review-blocker";
        body.Add(_blocker);

        _apply = NativeTheme.PrimaryButton("Apply and save once");
        _apply.AutomationId = "sr5-career-active-skill-apply";
        _apply.Clicked += async (_, _) =>
        {
            if (Interlocked.CompareExchange(ref _attempted, 1, 0) != 0)
            {
                return;
            }
            RefreshEnabledState();
            await RunAsync(ApplyAsync);
        };
        body.Add(_apply);
        body.Add(NativeTheme.Body(
            "A failed or uncertain apply is never retried from this quote. Reload the runner first because Core does not yet publish an idempotent retry receipt.",
            NativeTheme.Muted));
        Content = new ScrollView { Content = body };
        RefreshEnabledState();
    }

    protected override void Refresh() => RefreshEnabledState();

    private void RefreshEnabledState()
    {
        bool current = _draft.Matches(
            Coordinator.State.WorkspaceId,
            Coordinator.State.ContentRevision);
        bool attempted = Volatile.Read(ref _attempted) != 0;
        _apply.IsEnabled = current && !attempted;
        _blocker.Text = !current
            ? "The runner revision changed. This reviewed quote cannot be applied."
            : attempted
                ? "Apply was attempted once. Wait for the durable result; reload before any retry."
                : string.Empty;
    }

    private async Task ApplyAsync()
    {
        if (!_draft.Matches(Coordinator.State.WorkspaceId, Coordinator.State.ContentRevision))
        {
            await DisplayAlertAsync(
                "Stale review",
                "The runner changed. No mutation was attempted; reopen advancement.",
                "OK");
            return;
        }

        if (!Sr5CareerDraftCheckpointStore.TrySave(
                Sr5CareerDraftCheckpoint.FromDraft(
                    _draft,
                    Sr5CareerCheckpointPhase.Applying),
                out string checkpointBlocker))
        {
            await DisplayAlertAsync(
                "Apply blocked",
                $"The one-shot attempt could not be recovery-checkpointed. {checkpointBlocker}",
                "OK");
            return;
        }

        bool persisted = await Coordinator.ApplyCareerActiveSkillAdvanceAsync(_draft.ToRequest());
        if (!persisted)
        {
            Sr5CareerApplyResult unknown = Sr5CareerApplyResult.OutcomeUnknown(_draft);
            await DisplayAlertAsync(
                "Save not proven",
                $"{unknown.Message} Key {unknown.ActionPlan.IdempotencyKey}.",
                "OK");
            return;
        }

        if (!Sr5CareerApplyResult.TryCreateApplied(
                _draft,
                Coordinator.State.WorkspaceId,
                Coordinator.State.ContentRevision,
                Coordinator.State.SavedRevision,
                Coordinator.State.IsDirty,
                Coordinator.State.Progress?.Karma,
                Coordinator.State.Error,
                out Sr5CareerApplyResult result,
                out string blocker))
        {
            await DisplayAlertAsync("Receipt unavailable", blocker, "OK");
            return;
        }

        Sr5CareerActiveSkillReceipt receipt = result.Receipt!;
        if (!Sr5CareerDraftCheckpointStore.TryClear(
                result.ActionPlan.IdempotencyKey,
                out string clearBlocker))
        {
            await DisplayAlertAsync(
                "Saved; recovery cleanup needed",
                $"The advancement is saved, but its local checkpoint remains. {clearBlocker}",
                "OK");
        }

        await Navigation.PushAsync(new Sr5CareerActiveSkillReceiptPage(Coordinator, receipt));
    }

    private static string ShortDigest(string digest)
        => string.IsNullOrWhiteSpace(digest)
            ? "unavailable"
            : digest[..Math.Min(12, digest.Length)];
}

public sealed class Sr5CareerActiveSkillReceiptPage : NativePageBase
{
    private readonly Sr5CareerActiveSkillReceipt _receipt;
    private readonly Label _durability;

    public Sr5CareerActiveSkillReceiptPage(
        RunnerSessionCoordinator coordinator,
        Sr5CareerActiveSkillReceipt receipt) : base(coordinator)
    {
        _receipt = receipt ?? throw new ArgumentNullException(nameof(receipt));
        Title = "Advancement receipt";
        AutomationId = Sr5CareerWizardRoutes.ActiveSkillReceipt;

        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow("SR5 Career · 3 of 3"));
        body.Add(NativeTheme.Title("Saved advancement"));

        VerticalStackLayout details = new() { Spacing = 8 };
        details.Add(NativeTheme.Metric("Skill", receipt.SkillName));
        details.Add(NativeTheme.Metric(
            "Rating",
            $"{receipt.PreviousRating.ToString(CultureInfo.InvariantCulture)} → "
            + receipt.SavedRating.ToString(CultureInfo.InvariantCulture)));
        details.Add(NativeTheme.Metric("Karma spent", receipt.KarmaCost.ToString(CultureInfo.InvariantCulture)));
        details.Add(NativeTheme.Metric("Saved Karma", receipt.SavedKarma.ToString(CultureInfo.InvariantCulture)));
        details.Add(NativeTheme.Metric(
            "Saved revision",
            receipt.SavedContentRevision.ToString(CultureInfo.InvariantCulture)));
        details.Add(NativeTheme.Metric("Expense identity", receipt.ExpenseId.ToString("D")));
        details.Add(NativeTheme.Metric("Action identity", receipt.ActionId.ToString("D")));
        details.Add(NativeTheme.Metric("Undo type", receipt.KarmaUndoType));
        body.Add(NativeTheme.Card(details));

        _durability = NativeTheme.Body(string.Empty, NativeTheme.Muted);
        _durability.AutomationId = "sr5-career-active-skill-receipt-durability";
        body.Add(_durability);
        body.Add(NativeTheme.Body(
            $"Route {receipt.RouteId} · idempotency {receipt.IdempotencyKey} · "
            + $"rule {receipt.RuleDigest} · skill {receipt.SkillId:D} · source {receipt.SourceSkillId:D}",
            NativeTheme.Muted));
        Content = new ScrollView { Content = body };
        Refresh();
    }

    protected override void Refresh()
    {
        bool stillExact = Coordinator.State.WorkspaceId == _receipt.WorkspaceId
            && Coordinator.State.ContentRevision == _receipt.SavedContentRevision
            && Coordinator.State.SavedRevision == _receipt.SavedContentRevision
            && !Coordinator.State.IsDirty
            && Coordinator.State.Progress?.Karma == _receipt.SavedKarma;
        _durability.Text = stillExact
            ? "Receipt verified against the current clean saved revision."
            : "The runner moved past this receipt. The receipt describes the earlier saved revision only.";
        _durability.TextColor = stillExact ? NativeTheme.Muted : NativeTheme.Danger;
    }
}
