using System.Globalization;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;
using static Chummer.Android.Native.Sr5CareerFlowStrings;

namespace Chummer.Android.Native;

internal sealed class Sr5CareerLiveReviewedCheckpointAuthority(
    ISr5CareerCheckpointOwnerAuthority ownerAuthority,
    CareerActiveSkillAdvanceEditorState editor,
    Func<Sr5CareerRunnerBinding> currentBinding) :
    ISr5CareerReviewedCheckpointAuthority
{
    private readonly ISr5CareerCheckpointOwnerAuthority _ownerAuthority =
        ownerAuthority ?? throw new ArgumentNullException(nameof(ownerAuthority));
    private readonly CareerActiveSkillAdvanceEditorState _editor =
        editor ?? throw new ArgumentNullException(nameof(editor));
    private readonly Func<Sr5CareerRunnerBinding> _currentBinding =
        currentBinding ?? throw new ArgumentNullException(nameof(currentBinding));

    public Guid CurrentOwnerId => _ownerAuthority.CurrentOwnerId;

    public bool Owns(Sr5CareerDraftCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (!checkpoint.TryResume(
                _editor,
                out Sr5CareerActiveSkillDraft draft,
                out _)
            || !checkpoint.MatchesReviewedDraft(draft))
        {
            return false;
        }
        Sr5CareerReviewedCheckpointAccess currentAccess =
            Sr5CareerReviewedCheckpointAccess.FromCurrent(
                CurrentOwnerId,
                draft,
                _currentBinding());
        return currentAccess.Owns(checkpoint)
            && OwnsCurrentRunner(checkpoint);
    }

    public bool OwnsCurrentRunner(Sr5CareerDraftCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        Sr5CareerRunnerBinding binding = _currentBinding();
        if (!checkpoint.IsStructurallyValid()
            || CurrentOwnerId == Guid.Empty
            || checkpoint.OwnerId != CurrentOwnerId
            || !Sr5CareerWizardCatalog.IsSr5CareerRunner(
                binding.Created,
                binding.GameEdition)
            || binding.WorkspaceId?.Value != checkpoint.WorkspaceId)
        {
            return false;
        }

        return checkpoint.Phase switch
        {
            Sr5CareerCheckpointPhase.Reviewed =>
                OwnsExactCleanReviewedRevision(binding, checkpoint),
            Sr5CareerCheckpointPhase.Applying =>
                OwnsExactCleanReviewedRevision(binding, checkpoint)
                || OwnsExactSavedSuccessor(binding, checkpoint),
            Sr5CareerCheckpointPhase.Applied =>
                OwnsExactSavedSuccessor(binding, checkpoint),
            _ => false
        };
    }

    private static bool OwnsExactCleanReviewedRevision(
        Sr5CareerRunnerBinding binding,
        Sr5CareerDraftCheckpoint checkpoint)
        => binding.ContentRevision == checkpoint.ExpectedContentRevision
            && binding.SavedRevision == checkpoint.ExpectedContentRevision
            && !binding.IsDirty
            && string.IsNullOrWhiteSpace(binding.Error);

    private static bool OwnsExactSavedSuccessor(
        Sr5CareerRunnerBinding binding,
        Sr5CareerDraftCheckpoint checkpoint)
        => checkpoint.ExpectedContentRevision < long.MaxValue
            && binding.ContentRevision == checkpoint.ExpectedContentRevision + 1
            && binding.SavedRevision == binding.ContentRevision
            && !binding.IsDirty
            && string.IsNullOrWhiteSpace(binding.Error);
}

internal sealed record Sr5CareerActiveSkillWizardDependencies(
    Sr5CareerDraftCheckpointStore Store,
    ISr5CareerReviewedCheckpointAuthority ReviewedAuthority);

public sealed class Sr5CareerActiveSkillWizardPage : NativePageBase
{
    private readonly CareerActiveSkillAdvanceEditorState _editor;
    private readonly Sr5CareerActiveSkillCoordinator _authority;
    private readonly Sr5CareerDraftCheckpointStore _store;
    private readonly ISr5CareerReviewedCheckpointAuthority _reviewedAuthority;
    private readonly Picker _skills;
    private readonly Label _rating;
    private readonly Label _cost;
    private readonly Label _blocker;
    private readonly Label _recovery;
    private readonly Button _review;
    private readonly Button _resume;
    private readonly Button _resolve;
    private readonly Button _abandon;
    private CharacterCareerActiveSkillAdvanceQuote? _selected;
    private Sr5CareerActiveSkillDraft? _recoveryDraft;
    private Sr5CareerDraftCheckpoint? _checkpoint;
    private int _automaticResolutionStarted;

    public Sr5CareerActiveSkillWizardPage(
        RunnerSessionCoordinator coordinator,
        CareerActiveSkillAdvanceEditorState editor)
        : this(
            coordinator,
            editor,
            CreateDependencies(coordinator, editor))
    {
    }

    private Sr5CareerActiveSkillWizardPage(
        RunnerSessionCoordinator coordinator,
        CareerActiveSkillAdvanceEditorState editor,
        Sr5CareerActiveSkillWizardDependencies dependencies)
        : this(
            coordinator,
            editor,
            new Sr5CareerActiveSkillCoordinator(
                new RunnerSessionSr5CareerActiveSkillPresenter(coordinator),
                dependencies.ReviewedAuthority),
            dependencies.Store,
            dependencies.ReviewedAuthority)
    {
    }

    private static Sr5CareerActiveSkillWizardDependencies CreateDependencies(
        RunnerSessionCoordinator coordinator,
        CareerActiveSkillAdvanceEditorState editor)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(editor);
        PreferencesSr5CareerCheckpointOwnerAuthority ownerAuthority = new();
        Sr5CareerLiveReviewedCheckpointAuthority reviewedAuthority = new(
            ownerAuthority,
            editor,
            () => new RunnerSessionSr5CareerActiveSkillPresenter(coordinator).Binding);
        return new(
            Sr5CareerDraftCheckpointStore.CreateDefault(reviewedAuthority),
            reviewedAuthority);
    }

    internal Sr5CareerActiveSkillWizardPage(
        RunnerSessionCoordinator coordinator,
        CareerActiveSkillAdvanceEditorState editor,
        Sr5CareerActiveSkillCoordinator authority,
        Sr5CareerDraftCheckpointStore store,
        ISr5CareerReviewedCheckpointAuthority reviewedAuthority) : base(coordinator)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _authority = authority ?? throw new ArgumentNullException(nameof(authority));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _reviewedAuthority = reviewedAuthority ?? throw new ArgumentNullException(nameof(reviewedAuthority));
        Sr5CareerRunnerGuard.RequireCreated(
            new RunnerSessionSr5CareerActiveSkillPresenter(coordinator).Binding);
        if (coordinator.State.WorkspaceId != editor.WorkspaceId
            || coordinator.State.ContentRevision != editor.ContentRevision)
        {
            throw new InvalidOperationException(
                Text("The SR5 Active Skill route requires the current exact runner revision."));
        }

        _selected = editor.Skills.FirstOrDefault();
        Title = Text("Advance skill");
        AutomationId = Sr5CareerWizardRoutes.ActiveSkillChoose;

        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow(Text("SR5 Career · 1 of 3")));
        body.Add(NativeTheme.Title(Text("Choose an active skill")));
        body.Add(NativeTheme.Body(
            Text("Only exact saved skills from this created SR5 revision are shown. Core owns source resolution, rating limits, Karma cost and expense undo semantics."),
            NativeTheme.Muted));

        body.Add(NativeTheme.FieldLabel(Text("Active skill")));
        _skills = new Picker
        {
            AutomationId = "sr5-career-active-skill-picker",
            Title = Text("Saved active skill"),
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
                Format(
                    "{0} skill(s) are omitted because their exact authority cannot be reproduced safely.",
                    editor.OmittedSkillCount.ToString(CultureInfo.InvariantCulture)),
                NativeTheme.Danger);
            omitted.AutomationId = "sr5-career-active-skill-omitted";
            body.Add(NativeTheme.Card(omitted));
        }

        _review = NativeTheme.PrimaryButton(Text("Review advancement"));
        _review.AutomationId = "sr5-career-active-skill-review";
        _review.Clicked += async (_, _) => await RunAsync(OpenReviewAsync);
        body.Add(_review);

        _resume = NativeTheme.SecondaryButton(Text("Resume reviewed advancement"));
        _resume.AutomationId = "sr5-career-active-skill-resume";
        _resume.Clicked += async (_, _) => await RunAsync(ResumeReviewAsync);
        body.Add(_resume);

        _resolve = NativeTheme.PrimaryButton(Text("Resolve interrupted apply"));
        _resolve.AutomationId = "sr5-career-active-skill-resolve-outcome";
        _resolve.Clicked += async (_, _) => await RunAsync(ResolveCheckpointAsync);
        body.Add(_resolve);

        _abandon = NativeTheme.SecondaryButton(Text("Abandon reviewed draft"));
        _abandon.AutomationId = "sr5-career-active-skill-abandon-reviewed";
        _abandon.Clicked += async (_, _) => await RunAsync(AbandonReviewedAsync);
        body.Add(_abandon);

        Content = new ScrollView { Content = body };
        LoadRecoveryCheckpoint();
        RefreshEnabledState();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await Coordinator.InitializeAsync();
            // A child review page may have durably moved Applying back to
            // Reviewed, or forward to Applied, while this chooser was hidden.
            // Re-read the journal before deciding whether recovery may run.
            LoadRecoveryCheckpoint();
            RefreshEnabledState();
            if (_checkpoint?.Phase is (Sr5CareerCheckpointPhase.Applying
                or Sr5CareerCheckpointPhase.Applied)
                && _reviewedAuthority.OwnsCurrentRunner(_checkpoint)
                && Interlocked.CompareExchange(ref _automaticResolutionStarted, 1, 0) == 0)
            {
                await RunAsync(ResolveCheckpointAsync);
            }
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync(Text("Recovery unavailable"), exception.Message, Text("OK"));
        }
    }

    protected override void Refresh() => RefreshEnabledState();

    private static string SkillLabel(CharacterCareerActiveSkillAdvanceQuote skill)
        => Format(
            "{0} · {1} → {2} · {3} Karma",
            skill.Name,
            skill.TotalBaseRating.ToString(CultureInfo.InvariantCulture),
            (skill.TotalBaseRating + 1).ToString(CultureInfo.InvariantCulture),
            skill.KarmaCost.ToString(CultureInfo.InvariantCulture));

    private void SelectSkill()
    {
        _selected = _skills.SelectedIndex >= 0 && _skills.SelectedIndex < _editor.Skills.Count
            ? _editor.Skills[_skills.SelectedIndex]
            : null;
        RefreshEnabledState();
    }

    private void RefreshEnabledState()
    {
        bool sr5 = Sr5CareerWizardCatalog.IsSr5CareerRunner(
            Coordinator.State.Profile?.Created == true,
            Coordinator.State.Rules?.GameEdition);
        bool revisionMatches = sr5
            && Coordinator.State.WorkspaceId == _editor.WorkspaceId
            && Coordinator.State.ContentRevision == _editor.ContentRevision;
        bool reviewedOwned = TryAuthenticateReviewedCheckpoint(
            _checkpoint,
            _recoveryDraft,
            out _);
        _skills.IsEnabled = revisionMatches && _checkpoint is null && _editor.Skills.Count > 0;
        _rating.Text = _selected is null
            ? Text("No exact active-skill quote is available.")
            : Format(
                "Current {0} · after {1} · maximum {2}",
                _selected.TotalBaseRating.ToString(CultureInfo.InvariantCulture),
                (_selected.TotalBaseRating + 1).ToString(CultureInfo.InvariantCulture),
                _selected.RatingMaximum.ToString(CultureInfo.InvariantCulture));
        _cost.Text = _selected is null
            ? string.Empty
            : Format(
                "Cost {0} Karma · available {1} · after {2}",
                _selected.KarmaCost.ToString(CultureInfo.InvariantCulture),
                _selected.AvailableKarma.ToString(CultureInfo.InvariantCulture),
                (_selected.AvailableKarma - _selected.KarmaCost).ToString(CultureInfo.InvariantCulture));
        _blocker.Text = !sr5
            ? Text("This public action is available only to a created SR5 runner.")
            : !revisionMatches
                ? Text("This runner changed. Reopen advancement.")
                : _selected?.Blocker switch
                {
                    CharacterCareerActiveSkillAdvanceBlocker.AtMaximum =>
                        Text("This skill is already at its exact career maximum."),
                    CharacterCareerActiveSkillAdvanceBlocker.InsufficientKarma =>
                        Text("The runner does not have enough Karma for this advancement."),
                    _ => string.Empty
                };
        _review.IsEnabled = revisionMatches
            && _checkpoint is null
            && _selected is { CanAdvance: true }
            && CharacterCareerActiveSkillAdvanceRules.IsCoherent(_selected);
        _resume.IsVisible = reviewedOwned;
        _resume.IsEnabled = revisionMatches && reviewedOwned;
        _resolve.IsVisible = _checkpoint?.Phase is (Sr5CareerCheckpointPhase.Applying
            or Sr5CareerCheckpointPhase.Applied);
        _resolve.IsEnabled = _resolve.IsVisible
            && _checkpoint is not null
            && _reviewedAuthority.OwnsCurrentRunner(_checkpoint);
        _abandon.IsVisible = reviewedOwned;
        _abandon.IsEnabled = revisionMatches && reviewedOwned;
    }

    private async Task OpenReviewAsync()
    {
        Sr5CareerRunnerGuard.RequireCreated(
            new RunnerSessionSr5CareerActiveSkillPresenter(Coordinator).Binding);
        if (!Sr5CareerActiveSkillDraft.TryCreate(
                _editor,
                _selected,
                _reviewedAuthority.CurrentOwnerId,
                Guid.NewGuid(),
                DateTime.Now,
                out Sr5CareerActiveSkillDraft draft,
                out string blocker))
        {
            await DisplayAlertAsync(Text("Cannot review"), blocker, Text("OK"));
            return;
        }
        if (!draft.Matches(Coordinator.State.WorkspaceId, Coordinator.State.ContentRevision))
        {
            await DisplayAlertAsync(
                Text("Runner changed"),
                Text("Reopen active-skill advancement."),
                Text("OK"));
            return;
        }

        Sr5CareerDraftCheckpoint candidate = Sr5CareerDraftCheckpoint.FromDraft(draft);
        if (!_store.TryCreate(candidate, out Sr5CareerDraftCheckpoint stored, out blocker))
        {
            await DisplayAlertAsync(Text("Review not checkpointed"), blocker, Text("OK"));
            return;
        }
        _checkpoint = stored;
        _recoveryDraft = draft;
        await Navigation.PushAsync(new Sr5CareerActiveSkillReviewPage(
            Coordinator,
            draft,
            stored,
            _authority,
            _store,
            _reviewedAuthority));
    }

    private async Task ResumeReviewAsync()
    {
        if (_recoveryDraft is null
            || _checkpoint is null
            || _checkpoint.Phase != Sr5CareerCheckpointPhase.Reviewed
            || !TryAuthenticateReviewedCheckpoint(_checkpoint, _recoveryDraft, out _)
            || !_recoveryDraft.Matches(Coordinator.State.WorkspaceId, Coordinator.State.ContentRevision))
        {
            await DisplayAlertAsync(
                Text("Draft cannot resume"),
                Text("The saved review no longer owns this exact revision."),
                Text("OK"));
            return;
        }
        await Navigation.PushAsync(new Sr5CareerActiveSkillReviewPage(
            Coordinator,
            _recoveryDraft,
            _checkpoint,
            _authority,
            _store,
            _reviewedAuthority));
    }

    private async Task ResolveCheckpointAsync()
    {
        if (_checkpoint is null
            || _checkpoint.Phase is not (Sr5CareerCheckpointPhase.Applying
                or Sr5CareerCheckpointPhase.Applied)
            || !_reviewedAuthority.OwnsCurrentRunner(_checkpoint))
        {
            _recovery.Text = Text("This recovery lock belongs to another local owner or SR5 runner context.");
            _recovery.TextColor = NativeTheme.Danger;
            return;
        }

        Sr5CareerRecoveryResolution resolution =
            await _authority.ResolveAsync(_checkpoint);
        if (resolution.Status == Sr5CareerRecoveryStatus.OutcomeUnknown)
        {
            _recovery.Text = resolution.Message;
            _recovery.TextColor = NativeTheme.Danger;
            return;
        }

        Sr5CareerDraftCheckpoint stored = _checkpoint;
        if (_checkpoint.Phase == Sr5CareerCheckpointPhase.Applying
            && !_store.TryRecordAuthoritativeResolution(
                Sr5CareerCheckpointCas.From(_checkpoint),
                resolution,
                out stored,
                out string blocker))
        {
            _recovery.Text = blocker;
            _recovery.TextColor = NativeTheme.Danger;
            return;
        }
        _checkpoint = stored;

        if (resolution.Status == Sr5CareerRecoveryStatus.AppliedVerified
            && resolution.Receipt is { } receipt)
        {
            _recovery.Text = Text("The interrupted apply was found in fresh typed projections.");
            _recovery.TextColor = NativeTheme.Muted;
            await Navigation.PushAsync(new Sr5CareerActiveSkillReceiptPage(
                Coordinator,
                receipt,
                stored,
                _store,
                _reviewedAuthority));
            return;
        }

        LoadRecoveryCheckpoint();
        _recovery.Text = Text("Fresh typed projections prove the action was not saved. The reviewed draft may now be resumed.");
        _recovery.TextColor = NativeTheme.Muted;
    }

    private async Task AbandonReviewedAsync()
    {
        if (_checkpoint is null
            || _checkpoint.Phase != Sr5CareerCheckpointPhase.Reviewed
            || !TryAuthenticateReviewedCheckpoint(
                _checkpoint,
                _recoveryDraft,
                out _))
        {
            await DisplayAlertAsync(
                Text("Cannot abandon"),
                Text("Only the current authenticated SR5 owner and exact runner revision may abandon this Reviewed action."),
                Text("OK"));
            return;
        }
        bool confirmed = await DisplayAlertAsync(
            Text("Abandon reviewed draft?"),
            Text("This removes only the durable review checkpoint and does not change the runner."),
            Text("Abandon"),
            Text("Keep"));
        if (!confirmed)
        {
            return;
        }
        if (!_store.TryDeleteReviewed(
                Sr5CareerCheckpointCas.From(_checkpoint),
                out string blocker))
        {
            await DisplayAlertAsync(Text("Checkpoint not deleted"), blocker, Text("OK"));
            return;
        }
        _checkpoint = null;
        _recoveryDraft = null;
        _recovery.Text = string.Empty;
    }

    private void LoadRecoveryCheckpoint()
    {
        _checkpoint = null;
        _recoveryDraft = null;
        if (!_store.TryRead(out Sr5CareerDraftCheckpoint checkpoint, out string loadBlocker))
        {
            _recovery.Text = loadBlocker;
            _recovery.TextColor = string.IsNullOrWhiteSpace(loadBlocker)
                ? NativeTheme.Muted
                : NativeTheme.Danger;
            return;
        }
        if (checkpoint.Phase == Sr5CareerCheckpointPhase.Reviewed)
        {
            if (!checkpoint.TryResume(
                    _editor,
                    out Sr5CareerActiveSkillDraft draft,
                    out _)
                || !TryAuthenticateReviewedCheckpoint(checkpoint, draft, out _))
            {
                _recovery.Text = Text("A saved reviewed action is not authorized for this current SR5 owner and runner revision.");
                _recovery.TextColor = NativeTheme.Danger;
                return;
            }
            _checkpoint = checkpoint;
            _recoveryDraft = draft;
            _recovery.Text = Text("A durable reviewed advancement can be resumed with the same owner and action identity.");
            _recovery.TextColor = NativeTheme.Muted;
            int selectedIndex = _editor.Skills
                .Select((candidate, index) => (candidate, index))
                .First(pair => pair.candidate.Identity == draft.Quote.Identity)
                .index;
            _skills.SelectedIndex = selectedIndex;
            return;
        }
        if (!_reviewedAuthority.OwnsCurrentRunner(checkpoint))
        {
            _recovery.Text = Text("A saved apply lock is not authorized for this current local owner and SR5 runner.");
            _recovery.TextColor = NativeTheme.Danger;
            return;
        }
        _checkpoint = checkpoint;
        _recovery.Text = checkpoint.Phase switch
        {
            Sr5CareerCheckpointPhase.Applying =>
                Text("An interrupted apply is locked. Chummer is resolving the exact skill and expense outcome; it cannot be cleared or replayed."),
            Sr5CareerCheckpointPhase.Applied =>
                Text("A verified applied action is awaiting receipt acknowledgement."),
            _ => Text("The reviewed checkpoint no longer matches this runner revision.")
        };
        _recovery.TextColor = checkpoint.Phase == Sr5CareerCheckpointPhase.Reviewed
            ? NativeTheme.Danger
            : NativeTheme.Muted;
    }

    private bool TryAuthenticateReviewedCheckpoint(
        Sr5CareerDraftCheckpoint? checkpoint,
        Sr5CareerActiveSkillDraft? draft,
        out Sr5CareerReviewedCheckpointAccess currentAccess)
    {
        currentAccess = null!;
        if (checkpoint is null
            || draft is null
            || checkpoint.Phase != Sr5CareerCheckpointPhase.Reviewed
            || !checkpoint.MatchesReviewedDraft(draft))
        {
            return false;
        }
        currentAccess = Sr5CareerReviewedCheckpointAccess.FromCurrent(
            _reviewedAuthority.CurrentOwnerId,
            draft,
            new RunnerSessionSr5CareerActiveSkillPresenter(Coordinator).Binding);
        return currentAccess.Owns(checkpoint)
            && _reviewedAuthority.Owns(checkpoint);
    }
}

public sealed class Sr5CareerActiveSkillReviewPage : NativePageBase
{
    private readonly Sr5CareerActiveSkillDraft _draft;
    private Sr5CareerDraftCheckpoint _checkpoint;
    private readonly Sr5CareerActiveSkillCoordinator _authority;
    private readonly Sr5CareerDraftCheckpointStore _store;
    private readonly ISr5CareerReviewedCheckpointAuthority _reviewedAuthority;
    private readonly Button _apply;
    private readonly Label _blocker;
    private int _attempted;

    internal Sr5CareerActiveSkillReviewPage(
        RunnerSessionCoordinator coordinator,
        Sr5CareerActiveSkillDraft draft,
        Sr5CareerDraftCheckpoint checkpoint,
        Sr5CareerActiveSkillCoordinator authority,
        Sr5CareerDraftCheckpointStore store,
        ISr5CareerReviewedCheckpointAuthority reviewedAuthority) : base(coordinator)
    {
        _draft = draft ?? throw new ArgumentNullException(nameof(draft));
        _checkpoint = checkpoint ?? throw new ArgumentNullException(nameof(checkpoint));
        _authority = authority ?? throw new ArgumentNullException(nameof(authority));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _reviewedAuthority = reviewedAuthority ?? throw new ArgumentNullException(nameof(reviewedAuthority));
        Sr5CareerRunnerGuard.RequireCreated(
            new RunnerSessionSr5CareerActiveSkillPresenter(coordinator).Binding);
        if (!OwnsCurrentReviewedCheckpoint())
        {
            throw new InvalidOperationException(Text("The review does not own the durable Career checkpoint."));
        }

        Title = Text("Review advancement");
        AutomationId = Sr5CareerWizardRoutes.ActiveSkillReview;
        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow(Text("SR5 Career · 2 of 3")));
        body.Add(NativeTheme.Title(Text("Review exact diff")));

        VerticalStackLayout diff = new() { Spacing = 8 };
        diff.Add(NativeTheme.Metric(Text("Skill"), _draft.Quote.Name));
        diff.Add(NativeTheme.Metric(Text("Rating"), $"{_draft.Quote.TotalBaseRating} → {_draft.Quote.TotalBaseRating + 1}"));
        diff.Add(NativeTheme.Metric(Text("Karma"), $"{_draft.Quote.AvailableKarma} → {_draft.Plan.SavedCharacterKarma}"));
        diff.Add(NativeTheme.Metric(Text("Expense"), _draft.Plan.ExpenseReason));
        diff.Add(NativeTheme.Metric(Text("Date"), _draft.Plan.ExpenseDateLocal.ToString("O", CultureInfo.InvariantCulture)));
        diff.Add(NativeTheme.Metric(Text("Undo type"), _draft.Plan.KarmaUndoType));
        diff.Add(NativeTheme.Metric(Text("Skill identity"), _draft.Quote.Identity.SkillId.ToString("D")));
        diff.Add(NativeTheme.Metric(Text("Source identity"), _draft.Quote.Identity.SourceSkillId.ToString("D")));
        diff.Add(NativeTheme.Metric(Text("Expense identity"), _draft.Plan.ExpenseId.ToString("D")));
        body.Add(NativeTheme.Card(diff));

        _blocker = NativeTheme.Body(string.Empty, NativeTheme.Danger);
        body.Add(_blocker);
        _apply = NativeTheme.PrimaryButton(Text("Apply and verify once"));
        _apply.AutomationId = "sr5-career-active-skill-apply";
        _apply.Clicked += async (_, _) =>
        {
            if (Interlocked.CompareExchange(ref _attempted, 1, 0) == 0)
            {
                RefreshEnabledState();
                await RunAsync(ApplyAsync);
            }
        };
        body.Add(_apply);
        body.Add(NativeTheme.Body(
            Text("Before mutation, the exact owner/action checkpoint moves from Reviewed to Applying by CAS. A receipt appears only after fresh typed skill and expense reloads match every reviewed identity and value."),
            NativeTheme.Muted));
        Content = new ScrollView { Content = body };
        RefreshEnabledState();
    }

    protected override void Refresh() => RefreshEnabledState();

    private void RefreshEnabledState()
    {
        bool sr5 = Sr5CareerWizardCatalog.IsSr5CareerRunner(
            Coordinator.State.Profile?.Created == true,
            Coordinator.State.Rules?.GameEdition);
        bool current = sr5
            && _draft.Matches(Coordinator.State.WorkspaceId, Coordinator.State.ContentRevision)
            && OwnsCurrentReviewedCheckpoint();
        bool attempted = Volatile.Read(ref _attempted) != 0;
        _apply.IsEnabled = current && !attempted;
        _blocker.Text = !sr5
            ? Text("This public action boundary is no longer a created SR5 runner.")
            : !current
                ? Text("The runner revision or durable checkpoint changed. This review cannot apply.")
                : attempted
                    ? Text("The one-shot apply is in progress or awaiting authoritative recovery.")
                    : string.Empty;
    }

    private async Task ApplyAsync()
    {
        Sr5CareerRunnerGuard.RequireCreated(
            new RunnerSessionSr5CareerActiveSkillPresenter(Coordinator).Binding);
        if (!OwnsCurrentReviewedCheckpoint())
        {
            await DisplayAlertAsync(
                Text("Apply blocked"),
                Text("The current SR5 owner, runner revision, action or schema no longer owns this review."),
                Text("OK"));
            return;
        }
        if (!_store.TryBeginApply(
                Sr5CareerCheckpointCas.From(_checkpoint),
                out Sr5CareerDraftCheckpoint applying,
                out string blocker))
        {
            await DisplayAlertAsync(Text("Apply blocked"), blocker, Text("OK"));
            return;
        }
        _checkpoint = applying;

        Sr5CareerApplyResult result = await _authority.ApplyAsync(_draft, applying, _store);
        if (result.Status == Sr5CareerApplyStatus.OutcomeUnknown)
        {
            await DisplayAlertAsync(
                Text("Outcome unresolved"),
                Format("{0} This Applying checkpoint cannot be cleared or replayed.", result.Message),
                Text("OK"));
            return;
        }
        if (!_store.TryRecordAuthoritativeResolution(
                Sr5CareerCheckpointCas.From(applying),
                result.Resolution,
                out Sr5CareerDraftCheckpoint resolved,
                out blocker))
        {
            await DisplayAlertAsync(Text("Outcome not checkpointed"), blocker, Text("OK"));
            return;
        }
        _checkpoint = resolved;
        if (result.Status == Sr5CareerApplyStatus.RejectedBeforeMutation)
        {
            await DisplayAlertAsync(
                Text("Not applied"),
                Text("Fresh typed projections prove no mutation was saved. Reopen the reviewed draft before another attempt."),
                Text("OK"));
            return;
        }

        await Navigation.PushAsync(new Sr5CareerActiveSkillReceiptPage(
            Coordinator,
            result.Receipt!,
            resolved,
            _store,
            _reviewedAuthority));
    }

    private bool OwnsCurrentReviewedCheckpoint()
    {
        Sr5CareerReviewedCheckpointAccess currentAccess =
            Sr5CareerReviewedCheckpointAccess.FromCurrent(
                _reviewedAuthority.CurrentOwnerId,
                _draft,
                new RunnerSessionSr5CareerActiveSkillPresenter(Coordinator).Binding);
        return _checkpoint.MatchesReviewedDraft(_draft)
            && currentAccess.Owns(_checkpoint)
            && _reviewedAuthority.Owns(_checkpoint);
    }
}

public sealed class Sr5CareerActiveSkillReceiptPage : NativePageBase
{
    private readonly Sr5CareerActiveSkillReceipt _receipt;
    private readonly Sr5CareerDraftCheckpoint _checkpoint;
    private readonly Sr5CareerDraftCheckpointStore _store;
    private readonly ISr5CareerReviewedCheckpointAuthority _checkpointAuthority;
    private readonly Label _durability;

    internal Sr5CareerActiveSkillReceiptPage(
        RunnerSessionCoordinator coordinator,
        Sr5CareerActiveSkillReceipt receipt,
        Sr5CareerDraftCheckpoint checkpoint,
        Sr5CareerDraftCheckpointStore store,
        ISr5CareerReviewedCheckpointAuthority checkpointAuthority) : base(coordinator)
    {
        _receipt = receipt ?? throw new ArgumentNullException(nameof(receipt));
        _checkpoint = checkpoint ?? throw new ArgumentNullException(nameof(checkpoint));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _checkpointAuthority = checkpointAuthority ?? throw new ArgumentNullException(nameof(checkpointAuthority));
        if (!Sr5CareerDraftCheckpointStore.ReceiptMatchesCheckpoint(checkpoint, receipt)
            || !_checkpointAuthority.OwnsCurrentRunner(checkpoint))
        {
            throw new InvalidOperationException(Text("The typed receipt does not own the resolved checkpoint."));
        }

        Title = Text("Advancement receipt");
        AutomationId = Sr5CareerWizardRoutes.ActiveSkillReceipt;
        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow(Text("SR5 Career · 3 of 3")));
        body.Add(NativeTheme.Title(Text("Verified saved advancement")));
        VerticalStackLayout details = new() { Spacing = 8 };
        details.Add(NativeTheme.Metric(Text("Skill"), receipt.SkillName));
        details.Add(NativeTheme.Metric(Text("Rating"), $"{receipt.PreviousRating} → {receipt.SavedRating}"));
        details.Add(NativeTheme.Metric(Text("Karma spent"), receipt.KarmaCost.ToString(CultureInfo.InvariantCulture)));
        details.Add(NativeTheme.Metric(Text("Saved Karma"), receipt.SavedKarma.ToString(CultureInfo.InvariantCulture)));
        details.Add(NativeTheme.Metric(Text("Saved revision"), receipt.SavedContentRevision.ToString(CultureInfo.InvariantCulture)));
        details.Add(NativeTheme.Metric(Text("Expense identity"), receipt.ExpenseId.ToString("D")));
        details.Add(NativeTheme.Metric(Text("Expense date"), receipt.ExpenseDateLocal.ToString("O", CultureInfo.InvariantCulture)));
        details.Add(NativeTheme.Metric(Text("Expense reason"), receipt.ExpenseReason));
        details.Add(NativeTheme.Metric(Text("Expense type"), receipt.ExpenseType));
        details.Add(NativeTheme.Metric(Text("Refund"), receipt.ExpenseRefund.ToString(CultureInfo.InvariantCulture)));
        details.Add(NativeTheme.Metric(Text("Career visible"), receipt.ExpenseForceCareerVisible.ToString(CultureInfo.InvariantCulture)));
        details.Add(NativeTheme.Metric(Text("Karma undo type"), receipt.KarmaUndoType));
        details.Add(NativeTheme.Metric(Text("Nuyen undo type"), receipt.NuyenUndoType));
        details.Add(NativeTheme.Metric(Text("Undo object"), receipt.UndoObjectId));
        details.Add(NativeTheme.Metric(Text("Undo quantity"), receipt.UndoQuantity.ToString(CultureInfo.InvariantCulture)));
        details.Add(NativeTheme.Metric(Text("Undo extra"), receipt.UndoExtra));
        body.Add(NativeTheme.Card(details));
        _durability = NativeTheme.Body(string.Empty, NativeTheme.Muted);
        body.Add(_durability);
        body.Add(NativeTheme.Body(
            Format(
                "skill {0} · source {1} · source digest {2} · reviewed rule {3} · loaded rule {4} · loaded quote {5} · owner {6} · action {7}",
                receipt.SkillId.ToString("D"),
                receipt.SourceSkillId.ToString("D"),
                receipt.SourceRevision,
                receipt.ReviewedRuleDigest,
                receipt.RuleDigest,
                receipt.LogicalRevision,
                receipt.OwnerId.ToString("D"),
                receipt.ActionId.ToString("D")),
            NativeTheme.Muted));

        Button acknowledge = NativeTheme.PrimaryButton(Text("Acknowledge receipt"));
        acknowledge.AutomationId = "sr5-career-active-skill-receipt-acknowledge";
        acknowledge.Clicked += async (_, _) => await RunAsync(async () =>
        {
            if (!_store.TryDeleteApplied(
                    Sr5CareerCheckpointCas.From(_checkpoint),
                    _receipt,
                    out string blocker))
            {
                await DisplayAlertAsync(Text("Receipt remains pending"), blocker, Text("OK"));
                return;
            }
            await Navigation.PopToRootAsync();
        });
        body.Add(acknowledge);
        Content = new ScrollView { Content = body };
        Refresh();
    }

    protected override void Refresh()
    {
        bool stillExact = Coordinator.State.WorkspaceId == _receipt.WorkspaceId
            && Coordinator.State.ContentRevision == _receipt.SavedContentRevision
            && Coordinator.State.SavedRevision == _receipt.SavedContentRevision
            && !Coordinator.State.IsDirty;
        _durability.Text = stillExact
            ? Text("Receipt values came from fresh typed skill and expense projections for this clean saved revision.")
            : Text("The runner moved past this verified receipt; the receipt remains bound to its earlier saved revision.");
        _durability.TextColor = stillExact ? NativeTheme.Muted : NativeTheme.Danger;
    }
}
