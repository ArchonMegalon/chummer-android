using System.Globalization;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;
using static Chummer.Android.Native.Sr5CareerFlowStrings;

namespace Chummer.Android.Native;

internal sealed record Sr5CareerSpecializationWizardDependencies(
    Sr5CareerSpecializationCoordinator Coordinator,
    Sr5CareerSpecializationCheckpointStore Store,
    ISr5CareerSpecializationCheckpointAuthority CheckpointAuthority);

/// <summary>Step 1: choose one exact saved active or knowledge skill identity.</summary>
public sealed class Sr5CareerSpecializationWizardPage : NativePageBase
{
    private readonly CareerSkillSpecializationEditorState _editor;
    private readonly Sr5CareerSpecializationCoordinator _authority;
    private readonly Sr5CareerSpecializationCheckpointStore _store;
    private readonly ISr5CareerSpecializationCheckpointAuthority _checkpointAuthority;
    private readonly Picker _skills;
    private readonly Label _details;
    private readonly Label _recovery;
    private readonly Button _configure;
    private readonly Button _resume;
    private readonly Button _resolve;
    private readonly Button _abandon;
    private CareerSkillSpecializationCandidate? _selected;
    private Sr5CareerSpecializationCheckpoint? _checkpoint;

    public Sr5CareerSpecializationWizardPage(
        RunnerSessionCoordinator coordinator,
        CareerSkillSpecializationEditorState editor)
        : this(coordinator, editor, CreateDependencies(coordinator, editor))
    {
    }

    private Sr5CareerSpecializationWizardPage(
        RunnerSessionCoordinator coordinator,
        CareerSkillSpecializationEditorState editor,
        Sr5CareerSpecializationWizardDependencies dependencies)
        : base(coordinator)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _authority = dependencies.Coordinator;
        _store = dependencies.Store;
        _checkpointAuthority = dependencies.CheckpointAuthority;
        Sr5CareerRunnerGuard.RequireCreated(
            new RunnerSessionSr5CareerSpecializationPresenter(coordinator).Binding);
        if (coordinator.State.WorkspaceId != editor.WorkspaceId
            || coordinator.State.ContentRevision != editor.ContentRevision)
        {
            throw new InvalidOperationException(
                Text("The SR5 specialization route requires the current exact runner revision."));
        }

        _selected = editor.Skills.FirstOrDefault();
        Title = Text("Add specialization");
        AutomationId = Sr5CareerWizardRoutes.SpecializationChoose;
        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow(Text("SR5 Career · 1 of 4")));
        body.Add(NativeTheme.Title(Text("Choose a skill")));
        body.Add(NativeTheme.Body(
            Text("Only exact saved identities projected by Chummer are selectable. Active, sourced knowledge, and custom knowledge skills remain distinct."),
            NativeTheme.Muted));
        _skills = new Picker
        {
            AutomationId = "sr5-career-specialization-skill-picker",
            Title = Text("Saved skill"),
            ItemsSource = editor.Skills.Select(SkillLabel).ToArray(),
            SelectedIndex = editor.Skills.Count > 0 ? 0 : -1,
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        _skills.SelectedIndexChanged += (_, _) => SelectSkill();
        body.Add(_skills);
        _details = NativeTheme.Body(string.Empty, NativeTheme.Text);
        _details.AutomationId = "sr5-career-specialization-skill-details";
        body.Add(_details);
        if (editor.OmittedSkillCount > 0)
        {
            Label omitted = NativeTheme.Body(
                Format(
                    "{0} skill(s) were omitted because exact source authority was unavailable.",
                    editor.OmittedSkillCount.ToString(CultureInfo.InvariantCulture)),
                NativeTheme.Danger);
            omitted.AutomationId = "sr5-career-specialization-omitted";
            body.Add(NativeTheme.Card(omitted));
        }
        _recovery = NativeTheme.Body(string.Empty, NativeTheme.Muted);
        _recovery.AutomationId = "sr5-career-specialization-recovery";
        body.Add(_recovery);
        _configure = NativeTheme.PrimaryButton(Text("Choose specialization"));
        _configure.AutomationId = "sr5-career-specialization-configure";
        _configure.Clicked += async (_, _) => await RunAsync(OpenConfigureAsync);
        body.Add(_configure);
        _resume = NativeTheme.SecondaryButton(Text("Resume reviewed purchase"));
        _resume.AutomationId = "sr5-career-specialization-resume";
        _resume.Clicked += async (_, _) => await RunAsync(ResumeAsync);
        body.Add(_resume);
        _resolve = NativeTheme.PrimaryButton(Text("Resolve interrupted apply"));
        _resolve.AutomationId = "sr5-career-specialization-resolve";
        _resolve.Clicked += async (_, _) => await RunAsync(ResolveAsync);
        body.Add(_resolve);
        _abandon = NativeTheme.SecondaryButton(Text("Abandon reviewed purchase"));
        _abandon.AutomationId = "sr5-career-specialization-abandon";
        _abandon.Clicked += async (_, _) => await RunAsync(AbandonAsync);
        body.Add(_abandon);
        Content = new ScrollView { Content = body };
        LoadCheckpoint();
        RefreshEnabledState();
    }

    private static Sr5CareerSpecializationWizardDependencies CreateDependencies(
        RunnerSessionCoordinator coordinator,
        CareerSkillSpecializationEditorState editor)
    {
        PreferencesSr5CareerCheckpointOwnerAuthority owner = new();
        Sr5CareerSpecializationLiveCheckpointAuthority checkpointAuthority = new(
            owner,
            editor,
            () => new RunnerSessionSr5CareerSpecializationPresenter(coordinator).Binding);
        return new(
            new Sr5CareerSpecializationCoordinator(
                new RunnerSessionSr5CareerSpecializationPresenter(coordinator),
                owner),
            Sr5CareerSpecializationCheckpointStore.CreateDefault(checkpointAuthority),
            checkpointAuthority);
    }

    protected override void Refresh() => RefreshEnabledState();

    protected override Task PrepareForAppearanceRefreshAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LoadCheckpoint();
        RefreshEnabledState();
        return Task.CompletedTask;
    }

    private static string SkillLabel(CareerSkillSpecializationCandidate skill)
        => Format(
            "{0} · {1} · rating {2} · {3} existing",
            skill.SkillName,
            skill.Identity.Kind,
            skill.TotalBaseRating.ToString(CultureInfo.InvariantCulture),
            skill.ExistingSpecializationCount.ToString(CultureInfo.InvariantCulture));

    private void SelectSkill()
    {
        _selected = _skills.SelectedIndex >= 0 && _skills.SelectedIndex < _editor.Skills.Count
            ? _editor.Skills[_skills.SelectedIndex]
            : null;
        RefreshEnabledState();
    }

    private void RefreshEnabledState()
    {
        bool exact = Coordinator.State.WorkspaceId == _editor.WorkspaceId
            && Coordinator.State.ContentRevision == _editor.ContentRevision
            && Coordinator.State.SavedRevision == _editor.ContentRevision
            && !Coordinator.State.IsDirty;
        _details.Text = _selected is null
            ? Text("No exact specialization candidate is available.")
            : Format(
                "{0} · group {1} · {2} governed option(s) plus custom text",
                _selected.SkillCategory,
                _selected.SkillGroup.Length == 0 ? Text("none") : _selected.SkillGroup,
                _selected.AvailableOptions.Count.ToString(CultureInfo.InvariantCulture));
        _skills.IsEnabled = exact && _checkpoint is null;
        _configure.IsEnabled = exact && _selected is not null && _checkpoint is null;
        bool reviewed = _checkpoint?.Phase == Sr5CareerCheckpointPhase.Reviewed
            && _checkpointAuthority.OwnsReviewed(_checkpoint);
        _resume.IsVisible = reviewed;
        _resume.IsEnabled = reviewed;
        _abandon.IsVisible = reviewed;
        _abandon.IsEnabled = reviewed;
        bool interrupted = _checkpoint?.Phase is Sr5CareerCheckpointPhase.Applying
            or Sr5CareerCheckpointPhase.Applied;
        _resolve.IsVisible = interrupted;
        _resolve.IsEnabled = interrupted
            && _checkpoint is not null
            && _checkpointAuthority.OwnsCurrentRunner(_checkpoint);
    }

    private Task OpenConfigureAsync()
    {
        if (_selected is null)
        {
            return Task.CompletedTask;
        }
        return Navigation.PushAsync(new Sr5CareerSpecializationConfigurePage(
            Coordinator,
            _editor,
            _selected,
            _authority,
            _store,
            _checkpointAuthority));
    }

    private Task ResumeAsync()
    {
        if (_checkpoint is null
            || _checkpoint.Phase != Sr5CareerCheckpointPhase.Reviewed
            || !_checkpointAuthority.OwnsReviewed(_checkpoint))
        {
            return Task.CompletedTask;
        }
        return Navigation.PushAsync(new Sr5CareerSpecializationReviewPage(
            Coordinator,
            _checkpoint.Draft,
            _checkpoint,
            _authority,
            _store,
            _checkpointAuthority));
    }

    private async Task ResolveAsync()
    {
        if (_checkpoint is null || _checkpoint.Phase != Sr5CareerCheckpointPhase.Applying)
        {
            _recovery.Text = Text("A restarted Applied checkpoint has no persisted Core receipt and remains fail-closed.");
            _recovery.TextColor = NativeTheme.Danger;
            return;
        }
        Sr5CareerSpecializationResolution resolution = await _authority.ResolveAsync(_checkpoint);
        if (resolution.Status != Sr5CareerSpecializationRecoveryStatus.NotAppliedVerified)
        {
            _recovery.Text = resolution.Message;
            _recovery.TextColor = NativeTheme.Danger;
            return;
        }
        if (!_store.TryRecordNotApplied(
                Sr5CareerSpecializationCheckpointCas.From(_checkpoint),
                resolution,
                out Sr5CareerSpecializationCheckpoint stored,
                out string blocker))
        {
            _recovery.Text = blocker;
            _recovery.TextColor = NativeTheme.Danger;
            return;
        }
        _checkpoint = stored;
        _recovery.Text = Text("Fresh authority proved the mutation was not applied. The reviewed action can resume.");
        _recovery.TextColor = NativeTheme.Muted;
        RefreshEnabledState();
    }

    private async Task AbandonAsync()
    {
        if (_checkpoint is null || !_checkpointAuthority.OwnsReviewed(_checkpoint))
        {
            return;
        }
        bool confirmed = await DisplayAlertAsync(
            Text("Abandon reviewed specialization?"),
            Text("This removes only the durable review and does not change the runner."),
            Text("Abandon"),
            Text("Keep"));
        if (!confirmed)
        {
            return;
        }
        if (_store.TryDeleteReviewed(
                Sr5CareerSpecializationCheckpointCas.From(_checkpoint),
                out string blocker))
        {
            _checkpoint = null;
            _recovery.Text = string.Empty;
            RefreshEnabledState();
            return;
        }
        await DisplayAlertAsync(Text("Review not abandoned"), blocker, Text("OK"));
    }

    private void LoadCheckpoint()
    {
        _checkpoint = null;
        if (_store.TryRead(out Sr5CareerSpecializationCheckpoint checkpoint, out string blocker))
        {
            _checkpoint = checkpoint;
            _recovery.Text = checkpoint.Phase switch
            {
                Sr5CareerCheckpointPhase.Reviewed => Text("A durable reviewed specialization purchase can resume."),
                Sr5CareerCheckpointPhase.Applying => Text("An interrupted apply owns the runner until authoritative recovery succeeds."),
                Sr5CareerCheckpointPhase.Applied => Text("A current-process receipt was not acknowledged before restart; no persisted Core receipt exists, so this lock is retained."),
                _ => Text("The specialization checkpoint is unsupported.")
            };
            _recovery.TextColor = checkpoint.Phase == Sr5CareerCheckpointPhase.Reviewed
                ? NativeTheme.Muted
                : NativeTheme.Danger;
        }
        else
        {
            _recovery.Text = blocker;
            _recovery.TextColor = string.IsNullOrWhiteSpace(blocker) ? NativeTheme.Muted : NativeTheme.Danger;
        }
    }
}

/// <summary>Step 2: configure one source/improvement/weapon/custom selection and request a quote.</summary>
public sealed class Sr5CareerSpecializationConfigurePage : NativePageBase
{
    private readonly CareerSkillSpecializationEditorState _editor;
    private readonly CareerSkillSpecializationCandidate _candidate;
    private readonly Sr5CareerSpecializationCoordinator _authority;
    private readonly Sr5CareerSpecializationCheckpointStore _store;
    private readonly ISr5CareerSpecializationCheckpointAuthority _checkpointAuthority;
    private readonly Picker _options;
    private readonly Entry _custom;
    private readonly Label _selectionHelp;
    private readonly Button _quote;

    internal Sr5CareerSpecializationConfigurePage(
        RunnerSessionCoordinator coordinator,
        CareerSkillSpecializationEditorState editor,
        CareerSkillSpecializationCandidate candidate,
        Sr5CareerSpecializationCoordinator authority,
        Sr5CareerSpecializationCheckpointStore store,
        ISr5CareerSpecializationCheckpointAuthority checkpointAuthority) : base(coordinator)
    {
        _editor = editor;
        _candidate = candidate;
        _authority = authority;
        _store = store;
        _checkpointAuthority = checkpointAuthority;
        Title = Text("Choose specialization");
        AutomationId = Sr5CareerWizardRoutes.SpecializationConfigure;
        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow(Text("SR5 Career · 2 of 4")));
        body.Add(NativeTheme.Title(Format("Specialize {0}", _candidate.SkillName)));
        body.Add(NativeTheme.Body(
            Text("Catalog choices preserve their exact option identity and kind. Custom text is explicitly typed Custom and carries no fabricated option ID."),
            NativeTheme.Muted));
        string[] labels = _candidate.AvailableOptions
            .Select(option => $"{option.Name} · {option.Kind}")
            .Append(Text("Custom…"))
            .ToArray();
        _options = new Picker
        {
            AutomationId = "sr5-career-specialization-option-picker",
            Title = Text("Specialization option"),
            ItemsSource = labels,
            SelectedIndex = labels.Length > 0 ? 0 : -1,
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        _options.SelectedIndexChanged += (_, _) => RefreshEnabledState();
        body.Add(_options);
        _custom = NativeTheme.TextField(
            "sr5-career-specialization-custom-name",
            value: null,
            placeholder: Text("Custom specialization name"));
        _custom.TextChanged += (_, _) => RefreshEnabledState();
        body.Add(_custom);
        _selectionHelp = NativeTheme.Body(string.Empty, NativeTheme.Muted);
        _selectionHelp.AutomationId = "sr5-career-specialization-selection-help";
        body.Add(_selectionHelp);
        _quote = NativeTheme.PrimaryButton(Text("Review exact quote"));
        _quote.AutomationId = "sr5-career-specialization-quote";
        _quote.Clicked += async (_, _) => await RunAsync(QuoteAsync);
        body.Add(_quote);
        Content = new ScrollView { Content = body };
        RefreshEnabledState();
    }

    protected override void Refresh() => RefreshEnabledState();

    private bool IsCustom => _options.SelectedIndex == _candidate.AvailableOptions.Count;

    private void RefreshEnabledState()
    {
        bool exact = Coordinator.State.WorkspaceId == _editor.WorkspaceId
            && Coordinator.State.ContentRevision == _editor.ContentRevision
            && Coordinator.State.SavedRevision == _editor.ContentRevision
            && !Coordinator.State.IsDirty;
        _custom.IsVisible = IsCustom;
        _custom.IsEnabled = IsCustom && exact;
        _selectionHelp.Text = IsCustom
            ? Text("Custom is typed explicitly and is not treated as a source-catalog identity.")
            : _options.SelectedIndex >= 0 && _options.SelectedIndex < _candidate.AvailableOptions.Count
                ? _candidate.AvailableOptions[_options.SelectedIndex].SourceAnchor
                : Text("Choose one exact option.");
        _quote.IsEnabled = exact
            && _options.SelectedIndex >= 0
            && (!IsCustom || !string.IsNullOrWhiteSpace(_custom.Text));
    }

    private async Task QuoteAsync()
    {
        CharacterCareerSkillSpecializationSelection selection;
        if (IsCustom)
        {
            selection = new(
                _custom.Text?.Trim() ?? string.Empty,
                CharacterCareerSkillSpecializationOptionKind.Custom,
                OptionIdentity: null);
        }
        else if (_options.SelectedIndex >= 0
            && _options.SelectedIndex < _candidate.AvailableOptions.Count)
        {
            CharacterCareerSkillSpecializationOption option =
                _candidate.AvailableOptions[_options.SelectedIndex];
            selection = new(option.Name, option.Kind, option.OptionIdentity);
        }
        else
        {
            return;
        }

        CharacterCareerSkillSpecializationQuote? quote = await _authority.QuoteAsync(
            _editor,
            _candidate.Identity,
            selection);
        if (quote is null)
        {
            await DisplayAlertAsync(
                Text("Quote unavailable"),
                Text("Chummer could not reproduce this exact specialization selection."),
                Text("OK"));
            return;
        }
        if (!Sr5CareerSpecializationDraft.TryCreate(
                _editor,
                quote,
                _checkpointAuthority.CurrentOwnerId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                DateTime.Now,
                out Sr5CareerSpecializationDraft draft,
                out string blocker))
        {
            await DisplayAlertAsync(Text("Cannot review"), blocker, Text("OK"));
            return;
        }
        Sr5CareerSpecializationCheckpoint candidateCheckpoint =
            Sr5CareerSpecializationCheckpoint.FromDraft(draft);
        if (!_store.TryCreate(
                candidateCheckpoint,
                out Sr5CareerSpecializationCheckpoint stored,
                out blocker))
        {
            await DisplayAlertAsync(Text("Review not checkpointed"), blocker, Text("OK"));
            return;
        }
        await Navigation.PushAsync(new Sr5CareerSpecializationReviewPage(
            Coordinator,
            draft,
            stored,
            _authority,
            _store,
            _checkpointAuthority));
    }
}

/// <summary>Step 3: review the exact quote/plan and cross-lane CAS before mutation.</summary>
public sealed class Sr5CareerSpecializationReviewPage : NativePageBase
{
    private readonly Sr5CareerSpecializationDraft _draft;
    private Sr5CareerSpecializationCheckpoint _checkpoint;
    private readonly Sr5CareerSpecializationCoordinator _authority;
    private readonly Sr5CareerSpecializationCheckpointStore _store;
    private readonly ISr5CareerSpecializationCheckpointAuthority _checkpointAuthority;
    private readonly Label _status;
    private readonly Button _apply;

    internal Sr5CareerSpecializationReviewPage(
        RunnerSessionCoordinator coordinator,
        Sr5CareerSpecializationDraft draft,
        Sr5CareerSpecializationCheckpoint checkpoint,
        Sr5CareerSpecializationCoordinator authority,
        Sr5CareerSpecializationCheckpointStore store,
        ISr5CareerSpecializationCheckpointAuthority checkpointAuthority) : base(coordinator)
    {
        _draft = draft;
        _checkpoint = checkpoint;
        _authority = authority;
        _store = store;
        _checkpointAuthority = checkpointAuthority;
        Title = Text("Review specialization");
        AutomationId = Sr5CareerWizardRoutes.SpecializationReview;
        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow(Text("SR5 Career · 3 of 4")));
        body.Add(NativeTheme.Title(Format("{0}: {1}", draft.Quote.SkillName, draft.Quote.Selection.Name)));
        body.Add(NativeTheme.Card(
            NativeTheme.Body(
                Format(
                    "Typed identity: {0} / {1}\nSelection: {2} / {3}\nSpecializations: {4} → {5}\nKarma: {6} → {7} (cost {8})",
                    draft.Quote.Identity.Kind,
                    draft.Quote.Identity.SkillId.ToString("D"),
                    draft.Quote.Selection.Kind,
                    draft.Quote.Selection.OptionIdentity?.ToString() ?? Text("custom"),
                    draft.Quote.ExistingSpecializationCount.ToString(CultureInfo.InvariantCulture),
                    (draft.Quote.ExistingSpecializationCount + 1).ToString(CultureInfo.InvariantCulture),
                    draft.Quote.AvailableKarma.ToString(CultureInfo.InvariantCulture),
                    draft.Plan.SavedCharacterKarma.ToString(CultureInfo.InvariantCulture),
                    draft.Quote.KarmaCost.ToString(CultureInfo.InvariantCulture)),
                NativeTheme.Text)));
        if (draft.Quote.WillBreakSkillGroup)
        {
            body.Add(NativeTheme.Card(NativeTheme.Body(
                Text("This specialization will break the current skill group under the active setting."),
                NativeTheme.Danger)));
        }
        body.Add(NativeTheme.Body(
            Format(
                "Character {0} · source {1} · rules {2} · logical {3}",
                ShortDigest(draft.Quote.CharacterRevision),
                ShortDigest(draft.Quote.SourceRevision),
                ShortDigest(draft.Quote.RuleDigest),
                ShortDigest(draft.Quote.LogicalRevision)),
            NativeTheme.Muted));
        _status = NativeTheme.Body(
            Text("A durable shared mutation owner is acquired before Chummer receives the request."),
            NativeTheme.Muted);
        _status.AutomationId = "sr5-career-specialization-review-status";
        body.Add(_status);
        _apply = NativeTheme.PrimaryButton(Text("Confirm and save specialization"));
        _apply.AutomationId = "sr5-career-specialization-apply";
        _apply.Clicked += async (_, _) => await RunAsync(ApplyAsync);
        body.Add(_apply);
        Content = new ScrollView { Content = body };
        Refresh();
    }

    protected override void Refresh()
    {
        _apply.IsEnabled = _checkpoint.Phase == Sr5CareerCheckpointPhase.Reviewed
            && _checkpoint.MatchesActionDraft(_draft)
            && _checkpointAuthority.OwnsReviewed(_checkpoint)
            && _draft.Matches(Coordinator.State.WorkspaceId, Coordinator.State.ContentRevision);
    }

    private async Task ApplyAsync()
    {
        if (!_store.TryBeginApply(
                Sr5CareerSpecializationCheckpointCas.From(_checkpoint),
                out Sr5CareerSpecializationCheckpoint applying,
                out string blocker))
        {
            await DisplayAlertAsync(Text("Apply not started"), blocker, Text("OK"));
            return;
        }
        _checkpoint = applying;
        _apply.IsEnabled = false;
        Sr5CareerSpecializationApplyResult result = await _authority.ApplyAsync(
            _draft,
            applying,
            _store);
        if (result.Status != Sr5CareerSpecializationRecoveryStatus.AppliedVerifiedInCurrentProcess
            || result.Receipt is null)
        {
            _status.Text = result.Message;
            _status.TextColor = NativeTheme.Danger;
            return;
        }
        if (!_store.TryRecordImmediateApplied(
                Sr5CareerSpecializationCheckpointCas.From(applying),
                result.Receipt,
                out Sr5CareerSpecializationCheckpoint applied,
                out blocker))
        {
            _status.Text = blocker;
            _status.TextColor = NativeTheme.Danger;
            return;
        }
        _checkpoint = applied;
        await Navigation.PushAsync(new Sr5CareerSpecializationReceiptPage(
            Coordinator,
            _draft,
            result.Receipt,
            applied,
            _store,
            _checkpointAuthority));
    }

    private static string ShortDigest(string digest)
        => digest.Length >= 12 ? digest[..12] : digest;
}

/// <summary>Step 4: current-process verification and explicit acknowledgement.</summary>
public sealed class Sr5CareerSpecializationReceiptPage : NativePageBase
{
    private readonly Sr5CareerSpecializationDraft _draft;
    private readonly Sr5CareerSpecializationReceipt _receipt;
    private readonly Sr5CareerSpecializationCheckpoint _checkpoint;
    private readonly Sr5CareerSpecializationCheckpointStore _store;
    private readonly ISr5CareerSpecializationCheckpointAuthority _checkpointAuthority;
    private readonly Button _done;

    internal Sr5CareerSpecializationReceiptPage(
        RunnerSessionCoordinator coordinator,
        Sr5CareerSpecializationDraft draft,
        Sr5CareerSpecializationReceipt receipt,
        Sr5CareerSpecializationCheckpoint checkpoint,
        Sr5CareerSpecializationCheckpointStore store,
        ISr5CareerSpecializationCheckpointAuthority checkpointAuthority) : base(coordinator)
    {
        _draft = draft;
        _receipt = receipt;
        _checkpoint = checkpoint;
        _store = store;
        _checkpointAuthority = checkpointAuthority;
        Title = Text("Specialization saved");
        AutomationId = Sr5CareerWizardRoutes.SpecializationReceipt;
        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow(Text("SR5 Career · 4 of 4")));
        body.Add(NativeTheme.Title(Text("Saved and re-projected")));
        body.Add(NativeTheme.Body(
            Format(
                "{0} was added. Specializations {1} → {2}; Karma {3} → {4}.",
                receipt.SpecializationName,
                receipt.SpecializationCountBefore.ToString(CultureInfo.InvariantCulture),
                receipt.SpecializationCountAfter.ToString(CultureInfo.InvariantCulture),
                receipt.KarmaBefore.ToString(CultureInfo.InvariantCulture),
                receipt.KarmaAfter.ToString(CultureInfo.InvariantCulture)),
            NativeTheme.Text));
        body.Add(NativeTheme.Card(NativeTheme.Body(
            Text("This is a current-process typed projection proof, not a persisted Core receipt. If the process dies before acknowledgement, recovery remains locked rather than guessing."),
            NativeTheme.Muted)));
        _done = NativeTheme.PrimaryButton(Text("Acknowledge receipt"));
        _done.AutomationId = "sr5-career-specialization-receipt-acknowledge";
        _done.Clicked += async (_, _) => await RunAsync(AcknowledgeAsync);
        body.Add(_done);
        Content = new ScrollView { Content = body };
        Refresh();
    }

    protected override void Refresh()
    {
        _done.IsEnabled = _checkpoint.Phase == Sr5CareerCheckpointPhase.Applied
            && _checkpointAuthority.OwnsImmediateAppliedReceipt(_checkpoint, _receipt)
            && Sr5CareerSpecializationCoordinator.VerifiesReceipt(_draft, _receipt);
    }

    private async Task AcknowledgeAsync()
    {
        if (!_store.TryDeleteApplied(
                Sr5CareerSpecializationCheckpointCas.From(_checkpoint),
                _receipt,
                out string blocker))
        {
            await DisplayAlertAsync(Text("Receipt remains locked"), blocker, Text("OK"));
            return;
        }
        await Navigation.PopToRootAsync();
    }
}
