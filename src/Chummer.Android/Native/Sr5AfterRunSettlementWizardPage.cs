using System.Globalization;
using Chummer.Contracts.Characters;
using static Chummer.Android.Native.Sr5CareerFlowStrings;

namespace Chummer.Android.Native;

internal sealed record Sr5AfterRunSettlementWizardDependencies(
    Sr5AfterRunSettlementCoordinator Coordinator,
    Sr5AfterRunSettlementCheckpointStore Store,
    ISr5AfterRunSettlementCheckpointAuthority CheckpointAuthority);

/// <summary>Step 1: choose one exact completed-run proposal.</summary>
public sealed class Sr5AfterRunSettlementWizardPage : NativePageBase
{
    private readonly Sr5AfterRunSettlementEditorState _editor;
    private readonly Sr5AfterRunSettlementCoordinator _authority;
    private readonly Sr5AfterRunSettlementCheckpointStore _store;
    private readonly ISr5AfterRunSettlementCheckpointAuthority _checkpointAuthority;
    private readonly Picker _proposals;
    private readonly Label _details;
    private readonly Label _recovery;
    private readonly Button _continue;
    private readonly Button _resume;
    private readonly Button _resolve;
    private readonly Button _abandon;
    private Sr5AfterRunSettlementCandidate? _selected;
    private Sr5AfterRunSettlementCheckpoint? _checkpoint;

    public Sr5AfterRunSettlementWizardPage(
        RunnerSessionCoordinator coordinator,
        Sr5AfterRunSettlementEditorState editor)
        : this(coordinator, editor, CreateDependencies(coordinator, editor))
    {
    }

    private Sr5AfterRunSettlementWizardPage(
        RunnerSessionCoordinator coordinator,
        Sr5AfterRunSettlementEditorState editor,
        Sr5AfterRunSettlementWizardDependencies dependencies)
        : base(coordinator)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _authority = dependencies.Coordinator;
        _store = dependencies.Store;
        _checkpointAuthority = dependencies.CheckpointAuthority;
        Sr5CareerRunnerGuard.RequireCreated(
            new RunnerSessionSr5AfterRunSettlementPresenter(coordinator).Binding);
        if (!editor.IsExact()
            || coordinator.State.WorkspaceId != editor.WorkspaceId
            || coordinator.State.ContentRevision != editor.WorkspaceRevision
            || coordinator.State.SavedRevision != editor.WorkspaceRevision
            || coordinator.State.IsDirty)
        {
            throw new InvalidOperationException(
                Text("The SR5 After Run route requires the exact current clean saved runner revision."));
        }

        _selected = editor.Candidates.FirstOrDefault();
        Title = Text("After the run");
        AutomationId = Sr5CareerWizardRoutes.AfterRunChoose;
        VerticalStackLayout body = Body();
        body.Add(NativeTheme.Eyebrow(Text("SR5 Career · After Run · 1 of 7")));
        body.Add(NativeTheme.Title(Text("Choose the completed run")));
        body.Add(NativeTheme.Body(
            Text("Only governed proposal, run, and character IDs are selectable. This page never invents a run from the current character file."),
            NativeTheme.Muted));
        _proposals = new Picker
        {
            AutomationId = "sr5-after-run-proposal-picker",
            Title = Text("Completed run proposal"),
            ItemsSource = editor.Candidates.Select(CandidateLabel).ToArray(),
            SelectedIndex = editor.Candidates.Count > 0 ? 0 : -1,
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        _proposals.SelectedIndexChanged += (_, _) => SelectProposal();
        body.Add(_proposals);
        _details = NativeTheme.Body(string.Empty, NativeTheme.Text);
        _details.AutomationId = "sr5-after-run-proposal-context";
        body.Add(NativeTheme.Card(_details));
        if (editor.OmittedProposalCount > 0)
        {
            Label omitted = NativeTheme.Body(
                Format(
                    "{0} proposal(s) were omitted because exact catalog or Core quote authority was unavailable.",
                    editor.OmittedProposalCount.ToString(CultureInfo.InvariantCulture)),
                NativeTheme.Danger);
            omitted.AutomationId = "sr5-after-run-omitted-proposals";
            body.Add(NativeTheme.Card(omitted));
        }
        if (editor.Status != Sr5AfterRunCatalogStatus.Available)
        {
            Label unavailable = NativeTheme.Body(
                editor.Blockers.FirstOrDefault()
                    ?? Text("The governed After Run settlement authority is unavailable."),
                NativeTheme.Danger);
            unavailable.AutomationId = "sr5-after-run-authority-unavailable";
            body.Add(NativeTheme.Card(unavailable));
        }
        _recovery = NativeTheme.Body(string.Empty, NativeTheme.Muted);
        _recovery.AutomationId = "sr5-after-run-recovery-status";
        body.Add(_recovery);
        _continue = NativeTheme.PrimaryButton(Text("Review rewards"));
        _continue.AutomationId = "sr5-after-run-open-rewards";
        _continue.Clicked += async (_, _) => await RunAsync(OpenRewardsAsync);
        body.Add(_continue);
        _resume = NativeTheme.SecondaryButton(Text("Resume reviewed settlement"));
        _resume.AutomationId = "sr5-after-run-resume";
        _resume.Clicked += async (_, _) => await RunAsync(ResumeAsync);
        body.Add(_resume);
        _resolve = NativeTheme.PrimaryButton(Text("Resolve interrupted settlement"));
        _resolve.AutomationId = "sr5-after-run-resolve";
        _resolve.Clicked += async (_, _) => await RunAsync(ResolveAsync);
        body.Add(_resolve);
        _abandon = NativeTheme.SecondaryButton(Text("Abandon reviewed settlement"));
        _abandon.AutomationId = "sr5-after-run-abandon";
        _abandon.Clicked += async (_, _) => await RunAsync(AbandonAsync);
        body.Add(_abandon);
        Content = new ScrollView { Content = body };
        LoadCheckpoint();
        RefreshEnabledState();
    }

    private static Sr5AfterRunSettlementWizardDependencies CreateDependencies(
        RunnerSessionCoordinator coordinator,
        Sr5AfterRunSettlementEditorState editor)
    {
        var owner = new PreferencesSr5CareerCheckpointOwnerAuthority();
        var checkpointAuthority = new Sr5AfterRunSettlementLiveCheckpointAuthority(
            owner,
            editor,
            () => new RunnerSessionSr5AfterRunSettlementPresenter(coordinator).Binding);
        return new(
            new Sr5AfterRunSettlementCoordinator(
                new RunnerSessionSr5AfterRunSettlementPresenter(coordinator),
                owner),
            Sr5AfterRunSettlementCheckpointStore.CreateDefault(checkpointAuthority),
            checkpointAuthority);
    }

    internal static Page CreateEntryDestination(
        RunnerSessionCoordinator coordinator,
        Sr5AfterRunSettlementEditorState editor)
    {
        Sr5AfterRunSettlementWizardDependencies dependencies =
            CreateDependencies(coordinator, editor);
        bool ownsRecovery = dependencies.Store.TryReadOwnedRecovery(
            out _,
            out string recoveryBlocker);
        if (!ownsRecovery
            && string.IsNullOrWhiteSpace(recoveryBlocker)
            && editor.Status == Sr5AfterRunCatalogStatus.Missing
            && coordinator.SupportsManualAfterRunProposalEntry)
        {
            return new Sr5AfterRunManualProposalPage(
                coordinator,
                editor.WorkspaceId,
                editor.WorkspaceRevision);
        }

        return new Sr5AfterRunSettlementWizardPage(
            coordinator,
            editor,
            dependencies);
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

    private void SelectProposal()
    {
        _selected = _proposals.SelectedIndex >= 0
            && _proposals.SelectedIndex < _editor.Candidates.Count
                ? _editor.Candidates[_proposals.SelectedIndex]
                : null;
        RefreshEnabledState();
    }

    private void LoadCheckpoint()
    {
        if (_store.TryReadOwnedRecovery(
                out Sr5AfterRunSettlementCheckpoint checkpoint,
                out string blocker))
        {
            _checkpoint = checkpoint;
            _recovery.Text = checkpoint.Phase switch
            {
                Sr5CareerCheckpointPhase.Reviewed =>
                    Text("A reviewed settlement is durable and can be resumed or abandoned."),
                Sr5CareerCheckpointPhase.Applying =>
                    Text("An interrupted settlement owns the runner. Resolve the exact command before any other Career mutation."),
                Sr5CareerCheckpointPhase.Applied =>
                    Text("A verified Core receipt is durable and ready to inspect."),
                _ => Text("The durable After Run checkpoint is unavailable.")
            };
            _recovery.TextColor = checkpoint.Phase == Sr5CareerCheckpointPhase.Applying
                ? NativeTheme.Danger
                : NativeTheme.Muted;
        }
        else
        {
            _checkpoint = null;
            _recovery.Text = blocker;
            _recovery.TextColor = string.IsNullOrWhiteSpace(blocker)
                ? NativeTheme.Muted
                : NativeTheme.Danger;
        }
    }

    private void RefreshEnabledState()
    {
        bool exact = Coordinator.State.WorkspaceId == _editor.WorkspaceId
            && Coordinator.State.ContentRevision == _editor.WorkspaceRevision
            && Coordinator.State.SavedRevision == _editor.WorkspaceRevision
            && !Coordinator.State.IsDirty
            && string.IsNullOrWhiteSpace(Coordinator.State.Error);
        _details.Text = _selected is null
            ? Text("No exact completed-run proposal is available.")
            : Format(
                "{0} · completed {1}\nProposal {2}\nRun {3}\nStatus: {4}",
                _selected.RewardContext.RunTitle,
                _selected.RewardContext.CompletedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
                _selected.Quote.Identity.ProposalId.ToString("D"),
                _selected.Quote.Identity.RunId.ToString("D"),
                _selected.Quote.CanSettle
                    ? Text("all Core prerequisites satisfied")
                    : Sr5AfterRunSettlementDraft.BlockerText(_selected.Quote.Blocker));
        _proposals.IsEnabled = exact && _checkpoint is null;
        _continue.IsEnabled = exact
            && _editor.Status == Sr5AfterRunCatalogStatus.Available
            && _selected is not null
            && _checkpoint is null;
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

    private Task OpenRewardsAsync()
        => _selected is null
            ? Task.CompletedTask
            : Navigation.PushAsync(new Sr5AfterRunSettlementStagePage(
                Coordinator,
                _editor,
                _selected,
                Sr5AfterRunSettlementStage.Rewards,
                new(false, false, false, false, false, false),
                _authority,
                _store,
                _checkpointAuthority));

    private Task ResumeAsync()
        => _checkpoint is not null
            && _checkpoint.TryResume(_editor, out Sr5AfterRunSettlementDraft draft, out _)
                ? Navigation.PushAsync(new Sr5AfterRunSettlementReviewPage(
                    Coordinator,
                    _editor,
                    draft,
                    _checkpoint,
                    _authority,
                    _store,
                    _checkpointAuthority))
                : Task.CompletedTask;

    private async Task ResolveAsync()
    {
        if (_checkpoint is null)
        {
            return;
        }
        if (_checkpoint.Phase == Sr5CareerCheckpointPhase.Applied
            && _checkpoint.Receipt is { } persisted)
        {
            await Navigation.PushAsync(new Sr5AfterRunSettlementReceiptPage(
                Coordinator,
                _checkpoint,
                persisted,
                _store));
            return;
        }
        if (_checkpoint.Phase != Sr5CareerCheckpointPhase.Applying)
        {
            return;
        }
        Sr5AfterRunSettlementRecoveryResolution resolution = await _authority
            .ResolveAsync(_checkpoint, _store);
        if (resolution.Status == Sr5AfterRunSettlementRecoveryStatus.OutcomeUnknown)
        {
            _recovery.Text = resolution.Message;
            _recovery.TextColor = NativeTheme.Danger;
            return;
        }
        if (!_store.TryRecordAuthoritativeResolution(
                Sr5AfterRunSettlementCheckpointCas.From(_checkpoint),
                resolution,
                out Sr5AfterRunSettlementCheckpoint stored,
                out string blocker))
        {
            _recovery.Text = blocker;
            _recovery.TextColor = NativeTheme.Danger;
            return;
        }
        _checkpoint = stored;
        if (stored.Phase == Sr5CareerCheckpointPhase.Applied
            && stored.Receipt is { } receipt)
        {
            await Navigation.PushAsync(new Sr5AfterRunSettlementReceiptPage(
                Coordinator,
                stored,
                receipt,
                _store));
            return;
        }
        _recovery.Text = resolution.Message;
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
            Text("Abandon reviewed settlement?"),
            Text("This removes only the durable review. It does not change the runner or approve the proposal."),
            Text("Abandon"),
            Text("Keep"));
        if (!confirmed)
        {
            RefreshEnabledState();
            return;
        }
        if (_store.TryDeleteReviewed(
                Sr5AfterRunSettlementCheckpointCas.From(_checkpoint),
                out string blocker))
        {
            _checkpoint = null;
            _recovery.Text = Text("Reviewed settlement abandoned; no runner mutation occurred.");
            _recovery.TextColor = NativeTheme.Muted;
        }
        else
        {
            _recovery.Text = blocker;
            _recovery.TextColor = NativeTheme.Danger;
        }
        RefreshEnabledState();
    }

    private static string CandidateLabel(Sr5AfterRunSettlementCandidate candidate)
        => Format(
            "{0} · {1}",
            candidate.RewardContext.RunTitle,
            candidate.RewardContext.CompletedAt.ToLocalTime().ToString("d", CultureInfo.CurrentCulture));

    internal static VerticalStackLayout Body()
        => new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
}

internal enum Sr5AfterRunSettlementStage
{
    Rewards,
    Consequences,
    Contacts,
    GameMasterReview,
    OwnerReview
}

/// <summary>Steps 2–6: separate phone-deep review pages.</summary>
internal sealed class Sr5AfterRunSettlementStagePage : NativePageBase
{
    private readonly Sr5AfterRunSettlementEditorState _editor;
    private readonly Sr5AfterRunSettlementCandidate _candidate;
    private readonly Sr5AfterRunSettlementStage _stage;
    private readonly Sr5AfterRunReviewAcknowledgements _acknowledgements;
    private readonly Sr5AfterRunSettlementCoordinator _authority;
    private readonly Sr5AfterRunSettlementCheckpointStore _store;
    private readonly ISr5AfterRunSettlementCheckpointAuthority _checkpointAuthority;
    private readonly Button _continue;

    internal Sr5AfterRunSettlementStagePage(
        RunnerSessionCoordinator coordinator,
        Sr5AfterRunSettlementEditorState editor,
        Sr5AfterRunSettlementCandidate candidate,
        Sr5AfterRunSettlementStage stage,
        Sr5AfterRunReviewAcknowledgements acknowledgements,
        Sr5AfterRunSettlementCoordinator authority,
        Sr5AfterRunSettlementCheckpointStore store,
        ISr5AfterRunSettlementCheckpointAuthority checkpointAuthority)
        : base(coordinator)
    {
        _editor = editor;
        _candidate = candidate;
        _stage = stage;
        _acknowledgements = acknowledgements;
        _authority = authority;
        _store = store;
        _checkpointAuthority = checkpointAuthority;
        Title = StageTitle(stage);
        AutomationId = StageRoute(stage);
        VerticalStackLayout body = Sr5AfterRunSettlementWizardPage.Body();
        body.Add(NativeTheme.Eyebrow(Format("SR5 Career · After Run · {0} of 7", StageNumber(stage))));
        body.Add(NativeTheme.Title(StageTitle(stage)));
        AddStageContent(body);
        _continue = NativeTheme.PrimaryButton(ContinueLabel(stage));
        _continue.AutomationId = $"sr5-after-run-acknowledge-{StageToken(stage)}";
        _continue.Clicked += async (_, _) => await RunAsync(ContinueAsync);
        body.Add(_continue);
        Content = new ScrollView { Content = body };
        RefreshEnabledState();
    }

    protected override void Refresh() => RefreshEnabledState();

    private void AddStageContent(VerticalStackLayout body)
    {
        CharacterAfterRunSettlementQuote quote = _candidate.Quote;
        switch (_stage)
        {
            case Sr5AfterRunSettlementStage.Rewards:
                body.Add(NativeTheme.Body(
                    Text("These rewards come from the signed run context. This settlement only applies Heat, reputation, contacts and their Karma cost; it does not duplicate the reward ledger."),
                    NativeTheme.Muted));
                body.Add(NativeTheme.Card(new VerticalStackLayout
                {
                    Spacing = 7,
                    Children =
                    {
                        NativeTheme.Metric(Text("Run"), _candidate.RewardContext.RunTitle),
                        NativeTheme.Metric(Text("Karma award"), _candidate.RewardContext.KarmaAward.ToString(CultureInfo.InvariantCulture)),
                        NativeTheme.Metric(Text("Nuyen award"), _candidate.RewardContext.NuyenAward.ToString("N0", CultureInfo.CurrentCulture)),
                        NativeTheme.Metric(Text("Reward receipt"), Short(_candidate.RewardContext.RewardReceiptDigest))
                    }
                }));
                break;
            case Sr5AfterRunSettlementStage.Consequences:
                body.Add(NativeTheme.Body(
                    Text("Review every Core-calculated before → delta → after value. None is editable on this page."),
                    NativeTheme.Muted));
                body.Add(NativeTheme.Card(new VerticalStackLayout
                {
                    Spacing = 7,
                    Children =
                    {
                        Delta(Text("Heat"), quote.HeatBefore, quote.HeatDelta, quote.HeatAfter),
                        Delta(Text("Street Cred"), quote.StreetCredBefore, quote.StreetCredDelta, quote.StreetCredAfter),
                        Delta(Text("Notoriety"), quote.NotorietyBefore, quote.NotorietyDelta, quote.NotorietyAfter),
                        NativeTheme.Metric(Text("Public Awareness"), $"{quote.PublicAwarenessBefore} → {quote.PublicAwarenessAfter}"),
                        NativeTheme.Metric(Text("GM policy"), Short(quote.GmPolicyDigest))
                    }
                }));
                break;
            case Sr5AfterRunSettlementStage.Contacts:
                body.Add(NativeTheme.Body(
                    Text("Contacts retain stable IDs, origin kind, Connection, Loyalty and exact Karma cost."),
                    NativeTheme.Muted));
                if (quote.Contacts.Count == 0)
                {
                    body.Add(NativeTheme.Card(NativeTheme.Body(Text("No contacts are proposed."), NativeTheme.Muted)));
                }
                foreach (CharacterAfterRunContactSettlement contact in quote.Contacts)
                {
                    body.Add(NativeTheme.Card(new VerticalStackLayout
                    {
                        Spacing = 5,
                        Children =
                        {
                            NativeTheme.Title(contact.Name, 19),
                            NativeTheme.Body($"{contact.Role} · {contact.Location}", NativeTheme.Muted),
                            NativeTheme.Metric(Text("Connection / Loyalty"), $"{contact.Connection} / {contact.Loyalty}"),
                            NativeTheme.Metric(Text("Origin"), contact.Kind.ToString()),
                            NativeTheme.Metric(Text("Karma cost"), contact.KarmaCost.ToString(CultureInfo.InvariantCulture))
                        }
                    }));
                }
                body.Add(NativeTheme.Metric(Text("Total contact Karma"), quote.ContactKarmaCost.ToString(CultureInfo.InvariantCulture)));
                body.Add(NativeTheme.Metric(Text("Karma"), $"{quote.KarmaBefore} → {quote.KarmaAfter}"));
                break;
            case Sr5AfterRunSettlementStage.GameMasterReview:
                AddReview(body, CharacterAfterRunSettlementPrerequisite.GmApproved, quote.GmReviewDigest, Text("GM"));
                break;
            case Sr5AfterRunSettlementStage.OwnerReview:
                AddReview(body, CharacterAfterRunSettlementPrerequisite.OwnerApproved, quote.OwnerReviewDigest, Text("character owner"));
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private void AddReview(
        VerticalStackLayout body,
        CharacterAfterRunSettlementPrerequisite prerequisite,
        string digest,
        string actor)
    {
        bool approved = Approved(prerequisite);
        Label statement = NativeTheme.Body(
            approved
                ? Format("The exact {0} approval is present and digest-bound. Continue only after reviewing that approval.", actor)
                : Format("The exact {0} approval is missing or rejected. Android cannot create or infer it.", actor),
            approved ? NativeTheme.Text : NativeTheme.Danger);
        statement.AutomationId = $"sr5-after-run-{StageToken(_stage)}-authority";
        body.Add(NativeTheme.Card(statement));
        body.Add(NativeTheme.Metric(Text("Review digest"), Short(digest)));
        body.Add(NativeTheme.Metric(Text("Proposal logical digest"), Short(_candidate.Quote.LogicalDigest)));
    }

    private void RefreshEnabledState()
    {
        bool exact = Coordinator.State.WorkspaceId == _editor.WorkspaceId
            && Coordinator.State.ContentRevision == _editor.WorkspaceRevision
            && Coordinator.State.SavedRevision == _editor.WorkspaceRevision
            && !Coordinator.State.IsDirty
            && _candidate.IsExact(_editor.WorkspaceId, _editor.WorkspaceRevision);
        bool approved = _stage switch
        {
            Sr5AfterRunSettlementStage.GameMasterReview =>
                Approved(CharacterAfterRunSettlementPrerequisite.GmApproved),
            Sr5AfterRunSettlementStage.OwnerReview =>
                Approved(CharacterAfterRunSettlementPrerequisite.OwnerApproved),
            _ => true
        };
        _continue.IsEnabled = exact && approved;
    }

    private async Task ContinueAsync()
    {
        Sr5AfterRunReviewAcknowledgements next = _stage switch
        {
            Sr5AfterRunSettlementStage.Rewards => _acknowledgements with
            {
                RunContextReviewed = true,
                RewardsReviewed = true
            },
            Sr5AfterRunSettlementStage.Consequences => _acknowledgements with
            {
                ConsequencesReviewed = true
            },
            Sr5AfterRunSettlementStage.Contacts => _acknowledgements with
            {
                ContactsReviewed = true
            },
            Sr5AfterRunSettlementStage.GameMasterReview => _acknowledgements with
            {
                GmApprovalReviewed = true
            },
            Sr5AfterRunSettlementStage.OwnerReview => _acknowledgements with
            {
                OwnerApprovalReviewed = true
            },
            _ => throw new ArgumentOutOfRangeException()
        };
        if (_stage != Sr5AfterRunSettlementStage.OwnerReview)
        {
            await Navigation.PushAsync(new Sr5AfterRunSettlementStagePage(
                Coordinator,
                _editor,
                _candidate,
                (Sr5AfterRunSettlementStage)((int)_stage + 1),
                next,
                _authority,
                _store,
                _checkpointAuthority));
            return;
        }

        if (!Sr5AfterRunSettlementDraft.TryCreate(
                _editor,
                _candidate,
                _checkpointAuthority.CurrentOwnerId,
                Guid.NewGuid(),
                next,
                out Sr5AfterRunSettlementDraft draft,
                out string blocker))
        {
            await DisplayAlertAsync(Text("Settlement blocked"), blocker, Text("OK"));
            return;
        }
        Sr5AfterRunSettlementCheckpoint reviewed =
            Sr5AfterRunSettlementCheckpoint.FromDraft(draft);
        if (!_store.TryCreate(
                reviewed,
                out Sr5AfterRunSettlementCheckpoint stored,
                out blocker))
        {
            await DisplayAlertAsync(Text("Review checkpoint blocked"), blocker, Text("OK"));
            return;
        }
        await Navigation.PushAsync(new Sr5AfterRunSettlementReviewPage(
            Coordinator,
            _editor,
            draft,
            stored,
            _authority,
            _store,
            _checkpointAuthority));
    }

    private bool Approved(CharacterAfterRunSettlementPrerequisite prerequisite)
        => _candidate.Quote.Prerequisites.Count(candidate =>
                candidate.Prerequisite == prerequisite && candidate.Satisfied) == 1;

    private static Grid Delta(string name, int before, int delta, int after)
        => NativeTheme.Metric(
            name,
            $"{before.ToString(CultureInfo.InvariantCulture)} → {delta:+#;-#;0} → {after.ToString(CultureInfo.InvariantCulture)}");

    private static int StageNumber(Sr5AfterRunSettlementStage stage) => (int)stage + 2;

    private static string StageTitle(Sr5AfterRunSettlementStage stage)
        => stage switch
        {
            Sr5AfterRunSettlementStage.Rewards => Text("Review rewards"),
            Sr5AfterRunSettlementStage.Consequences => Text("Review Heat and reputation"),
            Sr5AfterRunSettlementStage.Contacts => Text("Review contact proposals"),
            Sr5AfterRunSettlementStage.GameMasterReview => Text("Review GM approval"),
            Sr5AfterRunSettlementStage.OwnerReview => Text("Review owner approval"),
            _ => throw new ArgumentOutOfRangeException(nameof(stage))
        };

    private static string ContinueLabel(Sr5AfterRunSettlementStage stage)
        => stage == Sr5AfterRunSettlementStage.OwnerReview
            ? Text("Create exact settlement review")
            : Format("Reviewed · continue to {0}", StageTitle((Sr5AfterRunSettlementStage)((int)stage + 1)));

    private static string StageToken(Sr5AfterRunSettlementStage stage)
        => stage switch
        {
            Sr5AfterRunSettlementStage.Rewards => "rewards",
            Sr5AfterRunSettlementStage.Consequences => "consequences",
            Sr5AfterRunSettlementStage.Contacts => "contacts",
            Sr5AfterRunSettlementStage.GameMasterReview => "gm-review",
            Sr5AfterRunSettlementStage.OwnerReview => "owner-review",
            _ => throw new ArgumentOutOfRangeException(nameof(stage))
        };

    private static string StageRoute(Sr5AfterRunSettlementStage stage)
        => stage switch
        {
            Sr5AfterRunSettlementStage.Rewards => Sr5CareerWizardRoutes.AfterRunRewards,
            Sr5AfterRunSettlementStage.Consequences => Sr5CareerWizardRoutes.AfterRunConsequences,
            Sr5AfterRunSettlementStage.Contacts => Sr5CareerWizardRoutes.AfterRunContacts,
            Sr5AfterRunSettlementStage.GameMasterReview => Sr5CareerWizardRoutes.AfterRunGmReview,
            Sr5AfterRunSettlementStage.OwnerReview => Sr5CareerWizardRoutes.AfterRunOwnerReview,
            _ => throw new ArgumentOutOfRangeException(nameof(stage))
        };

    private static string Short(string digest)
        => digest.Length <= 12 ? digest : digest[..12];
}

/// <summary>Step 7: explicit owner confirmation and atomic apply.</summary>
public sealed class Sr5AfterRunSettlementReviewPage : NativePageBase
{
    private readonly Sr5AfterRunSettlementEditorState _editor;
    private readonly Sr5AfterRunSettlementDraft _draft;
    private Sr5AfterRunSettlementCheckpoint _checkpoint;
    private readonly Sr5AfterRunSettlementCoordinator _authority;
    private readonly Sr5AfterRunSettlementCheckpointStore _store;
    private readonly ISr5AfterRunSettlementCheckpointAuthority _checkpointAuthority;
    private readonly Label _status;
    private readonly Button _apply;

    internal Sr5AfterRunSettlementReviewPage(
        RunnerSessionCoordinator coordinator,
        Sr5AfterRunSettlementEditorState editor,
        Sr5AfterRunSettlementDraft draft,
        Sr5AfterRunSettlementCheckpoint checkpoint,
        Sr5AfterRunSettlementCoordinator authority,
        Sr5AfterRunSettlementCheckpointStore store,
        ISr5AfterRunSettlementCheckpointAuthority checkpointAuthority)
        : base(coordinator)
    {
        _editor = editor;
        _draft = draft;
        _checkpoint = checkpoint;
        _authority = authority;
        _store = store;
        _checkpointAuthority = checkpointAuthority;
        Title = Text("Confirm settlement");
        AutomationId = Sr5CareerWizardRoutes.AfterRunReview;
        VerticalStackLayout body = Sr5AfterRunSettlementWizardPage.Body();
        body.Add(NativeTheme.Eyebrow(Text("SR5 Career · After Run · 7 of 7")));
        body.Add(NativeTheme.Title(Text("Review and confirm once")));
        body.Add(NativeTheme.Body(
            Text("This single atomic command applies only the Core plan below. The reward receipt stays external and is not replayed as manual Karma or Nuyen."),
            NativeTheme.Muted));
        CharacterAfterRunSettlementQuote quote = draft.Quote;
        body.Add(NativeTheme.Card(new VerticalStackLayout
        {
            Spacing = 7,
            Children =
            {
                NativeTheme.Metric(Text("Run"), draft.Candidate.RewardContext.RunTitle),
                NativeTheme.Metric(Text("Heat"), $"{quote.HeatBefore} → {quote.HeatAfter}"),
                NativeTheme.Metric(Text("Street Cred"), $"{quote.StreetCredBefore} → {quote.StreetCredAfter}"),
                NativeTheme.Metric(Text("Notoriety"), $"{quote.NotorietyBefore} → {quote.NotorietyAfter}"),
                NativeTheme.Metric(Text("Public Awareness"), $"{quote.PublicAwarenessBefore} → {quote.PublicAwarenessAfter}"),
                NativeTheme.Metric(Text("Contacts"), quote.Contacts.Count.ToString(CultureInfo.InvariantCulture)),
                NativeTheme.Metric(Text("Contact Karma"), quote.ContactKarmaCost.ToString(CultureInfo.InvariantCulture)),
                NativeTheme.Metric(Text("Karma"), $"{quote.KarmaBefore} → {quote.KarmaAfter}"),
                NativeTheme.Metric(Text("Binding"), Short(draft.Binding.BindingDigest)),
                NativeTheme.Metric(Text("Plan"), Short(draft.Plan.PlanDigest))
            }
        }));
        _status = NativeTheme.Body(
            Text("Reviewed checkpoint is durable. Applying reserves the shared Career mutation owner before calling Core."),
            NativeTheme.Muted);
        _status.AutomationId = "sr5-after-run-review-status";
        body.Add(_status);
        _apply = NativeTheme.PrimaryButton(Text("Confirm and settle once"));
        _apply.AutomationId = "sr5-after-run-confirm";
        _apply.Clicked += async (_, _) => await RunAsync(ApplyAsync);
        body.Add(_apply);
        Content = new ScrollView { Content = body };
        RefreshEnabledState();
    }

    protected override void Refresh() => RefreshEnabledState();

    private void RefreshEnabledState()
    {
        _apply.IsEnabled = _checkpoint.Phase == Sr5CareerCheckpointPhase.Reviewed
            && _checkpointAuthority.OwnsReviewed(_checkpoint)
            && _draft.Matches(
                Coordinator.State.WorkspaceId,
                Coordinator.State.ContentRevision)
            && Coordinator.State.SavedRevision == _draft.ExpectedWorkspaceRevision
            && !Coordinator.State.IsDirty;
    }

    private async Task ApplyAsync()
    {
        if (!_store.TryBeginApply(
                Sr5AfterRunSettlementCheckpointCas.From(_checkpoint),
                out Sr5AfterRunSettlementCheckpoint applying,
                out string blocker))
        {
            _status.Text = blocker;
            _status.TextColor = NativeTheme.Danger;
            return;
        }
        _checkpoint = applying;
        _apply.IsEnabled = false;
        Sr5AfterRunSettlementApplyResult result = await _authority.ApplyAsync(
            _draft,
            applying,
            _store);
        if (result.Status == Sr5AfterRunSettlementApplyStatus.OutcomeUnknown
            || !_store.TryRecordAuthoritativeResolution(
                Sr5AfterRunSettlementCheckpointCas.From(applying),
                result.Resolution,
                out Sr5AfterRunSettlementCheckpoint stored,
                out blocker))
        {
            _status.Text = result.Status == Sr5AfterRunSettlementApplyStatus.OutcomeUnknown
                ? result.Message
                : blocker;
            _status.TextColor = NativeTheme.Danger;
            return;
        }
        _checkpoint = stored;
        if (stored.Phase == Sr5CareerCheckpointPhase.Applied
            && stored.Receipt is { } receipt)
        {
            await Navigation.PushAsync(new Sr5AfterRunSettlementReceiptPage(
                Coordinator,
                stored,
                receipt,
                _store));
            return;
        }
        _status.Text = result.Message;
        _status.TextColor = NativeTheme.Danger;
        RefreshEnabledState();
    }

    private static string Short(string digest)
        => digest.Length <= 12 ? digest : digest[..12];
}

/// <summary>Durable, digest-bound Core receipt; acknowledgment removes only the journal.</summary>
public sealed class Sr5AfterRunSettlementReceiptPage : NativePageBase
{
    private readonly Sr5AfterRunSettlementCheckpoint _checkpoint;
    private readonly Sr5AfterRunSettlementCheckpointStore _store;
    private readonly Button _acknowledge;
    private readonly Label _status;

    internal Sr5AfterRunSettlementReceiptPage(
        RunnerSessionCoordinator coordinator,
        Sr5AfterRunSettlementCheckpoint checkpoint,
        CharacterAfterRunSettlementReceipt receipt,
        Sr5AfterRunSettlementCheckpointStore store)
        : base(coordinator)
    {
        if (checkpoint.Phase != Sr5CareerCheckpointPhase.Applied
            || !Sr5AfterRunSettlementCoordinator.ReceiptMatchesDraft(
                checkpoint.Draft,
                receipt))
        {
            throw new InvalidOperationException(
                Text("The After Run receipt page requires the exact durable Applied checkpoint."));
        }
        _checkpoint = checkpoint;
        _store = store;
        Title = Text("Settlement receipt");
        AutomationId = Sr5CareerWizardRoutes.AfterRunReceipt;
        VerticalStackLayout body = Sr5AfterRunSettlementWizardPage.Body();
        body.Add(NativeTheme.Eyebrow(Text("SR5 Career · After Run")));
        body.Add(NativeTheme.Title(Text("Settlement saved")));
        body.Add(NativeTheme.Body(
            Text("Core verified the exact post-save runner revision, transaction ledger and receipt. Acknowledging removes only this local recovery checkpoint."),
            NativeTheme.Muted));
        body.Add(NativeTheme.Card(new VerticalStackLayout
        {
            Spacing = 7,
            Children =
            {
                NativeTheme.Metric(Text("Transaction"), receipt.TransactionId.ToString("D")),
                NativeTheme.Metric(Text("Heat"), $"{receipt.HeatBefore} → {receipt.HeatAfter}"),
                NativeTheme.Metric(Text("Street Cred"), $"{receipt.StreetCredBefore} → {receipt.StreetCredAfter}"),
                NativeTheme.Metric(Text("Notoriety"), $"{receipt.NotorietyBefore} → {receipt.NotorietyAfter}"),
                NativeTheme.Metric(Text("Public Awareness"), $"{receipt.PublicAwarenessBefore} → {receipt.PublicAwarenessAfter}"),
                NativeTheme.Metric(Text("Karma"), $"{receipt.KarmaBefore} → {receipt.KarmaAfter}"),
                NativeTheme.Metric(Text("Contacts added"), receipt.AddedContacts.Count.ToString(CultureInfo.InvariantCulture)),
                NativeTheme.Metric(Text("Receipt"), receipt.ReceiptDigest)
            }
        }));
        _status = NativeTheme.Body(string.Empty, NativeTheme.Muted);
        _status.AutomationId = "sr5-after-run-receipt-status";
        body.Add(_status);
        _acknowledge = NativeTheme.PrimaryButton(Text("Acknowledge receipt"));
        _acknowledge.AutomationId = "sr5-after-run-receipt-acknowledge";
        _acknowledge.Clicked += async (_, _) => await RunAsync(AcknowledgeAsync);
        body.Add(_acknowledge);
        Content = new ScrollView { Content = body };
    }

    protected override void Refresh()
    {
        _acknowledge.IsEnabled = Coordinator.State.WorkspaceId
                == _checkpoint.Draft.WorkspaceId
            && Coordinator.State.ContentRevision
                == _checkpoint.Draft.ExpectedWorkspaceRevision + 1
            && Coordinator.State.SavedRevision == Coordinator.State.ContentRevision
            && !Coordinator.State.IsDirty;
    }

    private async Task AcknowledgeAsync()
    {
        if (!_store.TryDeleteApplied(
                Sr5AfterRunSettlementCheckpointCas.From(_checkpoint),
                out string blocker))
        {
            _status.Text = blocker;
            _status.TextColor = NativeTheme.Danger;
            return;
        }
        _acknowledge.IsEnabled = false;
        _status.Text = Text("Receipt acknowledged. The saved runner and Core transaction ledger remain unchanged.");
        _status.TextColor = NativeTheme.Muted;
        await Navigation.PopToRootAsync();
    }
}
