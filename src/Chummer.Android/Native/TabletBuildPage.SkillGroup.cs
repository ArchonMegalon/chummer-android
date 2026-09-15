using System.Globalization;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;
using static Chummer.Android.Native.Sr5CareerFlowStrings;

namespace Chummer.Android.Native;

public sealed partial class TabletBuildPage
{
    private readonly Func<ISr5CareerSkillGroupCheckpointAuthority, Sr5CareerSkillGroupCheckpointStore> _skillGroupStoreFactory;
    private readonly Func<Func<Task<CharacterCareerSkillGroupAdvanceResult?>>, Task<CharacterCareerSkillGroupAdvanceResult?>> _skillGroupDispatch;
    private ISr5CareerCheckpointOwnerAuthority? _skillGroupOwner;
    private CareerSkillGroupAdvanceEditorState? _skillGroupEditor;
    private CharacterOverviewState? _skillGroupExpected;
    private CharacterCareerSkillGroupIdentity? _selectedSkillGroup;
    private CancellationTokenSource? _skillGroupLoadOperation;
    private Label? _skillGroupLoadStatus;
    private CancellationTokenSource? _skillGroupOperation;
    private TabletSkillGroupReview? _skillGroupReview;

    private void InvalidateTabletSkillGroupReview()
    {
        _skillGroupReview = null;
        CancellationTokenSource? operation = _skillGroupOperation;
        _skillGroupOperation = null;
        operation?.Cancel();
        operation?.Dispose();
        // Departure and selection changes never delete a durable recovery lock.
    }

    protected override bool TryDeferCoordinatorRefresh()
        => _skillGroupReview is { Checkpoint: not null } review
            && IsTabletSkillGroupContextCurrent(review, allowSuccessor: true);

    private bool AddTabletSkillGroupCollection(CharacterOverviewState expected, long generation)
    {
        if (!string.Equals(expected.ActiveSectionId, "skills", StringComparison.Ordinal)
            || !Sr5CareerWizardCatalog.IsSr5CareerRunner(expected.Profile?.Created == true, expected.Rules?.GameEdition))
        {
            _skillGroupEditor = null;
            _skillGroupExpected = null;
            _selectedSkillGroup = null;
            return false;
        }

        if (_skillGroupExpected is not null && !IsCurrentView(_skillGroupExpected))
        {
            _skillGroupEditor = null;
            _skillGroupExpected = null;
            _selectedSkillGroup = null;
        }
        Button load = NativeTheme.SecondaryButton(Text("Advance skill group"));
        load.AutomationId = "tablet-career-skill-groups-load";
        string readiness = TabletSkillGroupLoadReadiness(expected);
        load.IsEnabled = string.IsNullOrWhiteSpace(readiness);
        load.Clicked += async (_, _) =>
        {
            if (!ReferenceEquals(load.Parent, _collection) || !IsCurrentCollection(generation, expected)) return;
            await RunWithConditionalRefreshAsync(() => LoadTabletSkillGroupsAsync(expected, generation));
        };
        _collection.Add(load);
        _skillGroupLoadStatus = NativeTheme.Body(readiness, NativeTheme.Danger);
        _skillGroupLoadStatus.AutomationId = "tablet-career-skill-groups-status";
        _collection.Add(_skillGroupLoadStatus);
        if (_skillGroupEditor is not { } editor) return false;

        _collection.Add(NativeTheme.Title(Text("Choose a skill group"), 23));
        if (editor.OmittedSkillGroupCount > 0)
            _collection.Add(NativeTheme.Body(Format(
                "{0} skill-group quote(s) were omitted because exact authority could not be reproduced.",
                editor.OmittedSkillGroupCount), NativeTheme.Danger));
        foreach (CharacterCareerSkillGroupAdvanceQuote quote in editor.SkillGroups)
        {
            bool exact = CharacterCareerSkillGroupAdvanceRules.IsCoherent(quote)
                && editor.SkillGroups.Count(candidate => candidate.Identity == quote.Identity) == 1;
            Border row = NativeTheme.NavigationRow(quote.Name,
                $"{quote.GroupRating} → {quote.TargetGroupRating} · {quote.Identity.InternalId:D}", () =>
                {
                    if (!exact || !IsCurrentCollection(generation, expected)
                        || !ReferenceEquals(_skillGroupEditor, editor)
                        || !Coordinator.IsTabletCareerSkillGroupOwnerCurrent(expected)) return Task.CompletedTask;
                    _selectedSkillGroup = quote.Identity;
                    BuildCollectionPane(Coordinator.State.ActiveCollectionEditor);
                    BuildInspectorPane(Coordinator.State.ActiveCollectionEditor);
                    return Task.CompletedTask;
                }, exact, $"tablet-career-skill-group-{quote.Identity.InternalId:D}");
            row.BackgroundColor = _selectedSkillGroup == quote.Identity ? NativeTheme.SignalSoft : NativeTheme.Surface;
            _collection.Add(row);
        }
        if (editor.SkillGroups.Count == 0)
            _collection.Add(NativeTheme.Body(Text("No exact skill-group quote is available."), NativeTheme.Muted));
        Button skills = NativeTheme.SecondaryButton(Text("Active skills"));
        skills.AutomationId = "tablet-career-skill-groups-close";
        skills.Clicked += (_, _) =>
        {
            if (!ReferenceEquals(skills.Parent, _collection) || !IsCurrentCollection(generation, expected)) return;
            _skillGroupEditor = null;
            _skillGroupExpected = null;
            _selectedSkillGroup = null;
            BuildCollectionPane(Coordinator.State.ActiveCollectionEditor);
            BuildInspectorPane(Coordinator.State.ActiveCollectionEditor);
        };
        _collection.Add(skills);
        return true;
    }

    private async Task<bool> LoadTabletSkillGroupsAsync(CharacterOverviewState expected, long generation)
    {
        if (_skillGroupLoadOperation is not null || !IsCurrentCollection(generation, expected)) return false;
        Label? status = _skillGroupLoadStatus;
        string readiness = TabletSkillGroupLoadReadiness(expected);
        if (!string.IsNullOrWhiteSpace(readiness))
        {
            Report(readiness);
            return false;
        }
        using CancellationTokenSource operation = new();
        _skillGroupLoadOperation = operation;
        ISr5CareerCheckpointOwnerAuthority owner = _skillGroupOwner ??= new PreferencesSr5CareerCheckpointOwnerAuthority();
        Guid ownerId = owner.CurrentOwnerId;
        try
        {
            CareerSkillGroupAdvanceEditorState? editor = await new Sr5CareerSkillGroupCoordinator(
                new RunnerSessionSr5CareerSkillGroupPresenter(Coordinator), owner).PrepareAsync(operation.Token);
            if (operation.IsCancellationRequested || !IsCurrentCollection(generation, expected)) return false;
            if (!Coordinator.IsTabletCareerSkillGroupOwnerCurrent(expected) || owner.CurrentOwnerId != ownerId)
            {
                Report(Text("Unavailable until a complete typed transaction authority is bound to this exact runner revision."));
                return false;
            }
            if (editor is null || editor.WorkspaceId != expected.WorkspaceId
                || editor.ContentRevision != expected.ContentRevision)
            {
                Report(Text("No exact skill-group quote is available."));
                return false;
            }
            _skillGroupEditor = editor;
            _skillGroupExpected = expected;
            // A collection load never silently selects the first group or a skill's group name.
            _selectedSkillGroup = null;
            BuildCollectionPane(Coordinator.State.ActiveCollectionEditor);
            BuildInspectorPane(Coordinator.State.ActiveCollectionEditor);
            return false;
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Report(Text("No exact skill-group quote is available."));
            return false;
        }
        finally
        {
            if (ReferenceEquals(_skillGroupLoadOperation, operation)) _skillGroupLoadOperation = null;
        }

        void Report(string message)
        {
            if (status is not null && ReferenceEquals(status, _skillGroupLoadStatus)
                && ReferenceEquals(status.Parent, _collection) && IsCurrentCollection(generation, expected))
                status.Text = message;
        }
    }

    private string TabletSkillGroupLoadReadiness(CharacterOverviewState expected)
        => expected.IsDirty || expected.ContentRevision <= 0 || expected.SavedRevision != expected.ContentRevision
            ? Text("Prerequisites: the exact saved runner revision and this listed typed action must remain available.")
            : Coordinator.IsBusy || expected.Error is not null || !Coordinator.IsTabletCareerSkillGroupOwnerCurrent(expected)
                ? Text("Unavailable until a complete typed transaction authority is bound to this exact runner revision.")
                : string.Empty;

    private bool BuildTabletSkillGroupInspector(CharacterOverviewState expected, long generation)
    {
        if (_skillGroupEditor is not { } editor || _skillGroupExpected is not { } loaded
            || !IsCurrentView(loaded)) return false;
        if (_selectedSkillGroup is null)
        {
            _inspector.Add(NativeTheme.Title(Text("Choose a skill group"), 23));
            return true;
        }
        CharacterCareerSkillGroupAdvanceQuote[] matches = editor.SkillGroups
            .Where(quote => quote.Identity == _selectedSkillGroup).Take(2).ToArray();
        if (matches.Length != 1 || !CharacterCareerSkillGroupAdvanceRules.IsCoherent(matches[0])
            || !CharacterCareerSkillGroupAdvanceServiceIntegrity.TryComputeBindingDigest(
                editor.WorkspaceId, editor.ContentRevision, matches[0], out string digest))
        {
            _inspector.Add(NativeTheme.Body(Text("No exact skill-group quote is available."), NativeTheme.Danger));
            return true;
        }
        _skillGroupOperation = new();
        ISr5CareerCheckpointOwnerAuthority owner = _skillGroupOwner ??= new PreferencesSr5CareerCheckpointOwnerAuthority();
        RunnerSessionSr5CareerSkillGroupPresenter presenter = new(Coordinator);
        TabletSkillGroupReview review = new(expected, generation, editor, matches[0], digest,
            owner.CurrentOwnerId, _skillGroupOperation.Token);
        Sr5CareerSkillGroupLiveCheckpointAuthority live = new(owner, editor, () => presenter.Binding);
        review.CheckpointAuthority = new TabletSkillGroupCheckpointAuthority(live,
            () => IsTabletSkillGroupContextCurrent(review, false),
            () => IsTabletSkillGroupContextCurrent(review, true));
        review.Store = _skillGroupStoreFactory(review.CheckpointAuthority);
        review.Authority = new(new TabletSkillGroupPresenter(this, review, presenter), owner);
        _skillGroupReview = review;
        if (review.Store.TryRead(out Sr5CareerSkillGroupCheckpoint checkpoint, out string blocker))
        {
            if (checkpoint.Draft.Quote.Identity != review.SelectedQuote.Identity
                || !review.CheckpointAuthority.OwnsCurrentRunner(checkpoint))
                review.Blocker = Text("Another owner, workspace, revision, or skill-group action owns the checkpoint.");
            else review.Checkpoint = checkpoint;
        }
        else review.Blocker = blocker;
        RenderTabletSkillGroupReview(review);
        _inspector.Add(review.Panel);
        return true;
    }

    private bool IsTabletSkillGroupContextCurrent(TabletSkillGroupReview review, bool allowSuccessor)
    {
        CharacterOverviewState current = Coordinator.State;
        CharacterOverviewState expected = review.Expected;
        bool revision = current.ContentRevision == expected.ContentRevision
            || allowSuccessor && expected.ContentRevision < long.MaxValue
                && current.ContentRevision == expected.ContentRevision + 1;
        if (!ReferenceEquals(_skillGroupReview, review) || review.Token.IsCancellationRequested || _departed
            || _inspectorGeneration != review.Generation || _selectedSkillGroup != review.SelectedQuote.Identity
            || !ReferenceEquals(_skillGroupEditor, review.Editor) || _skillGroupOwner?.CurrentOwnerId != review.OwnerId
            || !Coordinator.IsTabletCareerSkillGroupOwnerCurrent(expected)
            || current.WorkspaceId != expected.WorkspaceId || !revision
            || current.SavedRevision != current.ContentRevision || current.IsDirty || current.Error is not null
            || !string.Equals(current.ActiveSectionId, expected.ActiveSectionId, StringComparison.Ordinal)
            || !Sr5CareerWizardCatalog.IsSr5CareerRunner(current.Profile?.Created == true, current.Rules?.GameEdition)) return false;
        if (current.ContentRevision == expected.ContentRevision
            && (!ReferenceEquals(current.ActiveCollectionEditor, expected.ActiveCollectionEditor)
                || !string.Equals(current.ActiveSectionJson, expected.ActiveSectionJson, StringComparison.Ordinal))) return false;
        CharacterCareerSkillGroupAdvanceQuote[] matches = review.Editor.SkillGroups
            .Where(quote => quote.Identity == review.SelectedQuote.Identity).Take(2).ToArray();
        return matches.Length == 1
            && CharacterCareerSkillGroupAdvanceServiceIntegrity.TryComputeBindingDigest(
                review.Editor.WorkspaceId, review.Editor.ContentRevision, matches[0], out string digest)
            && string.Equals(digest, review.SelectedDigest, StringComparison.Ordinal);
    }

    private void RenderTabletSkillGroupReview(TabletSkillGroupReview review)
    {
        VerticalStackLayout panel = review.Panel;
        panel.Clear();
        CharacterCareerSkillGroupAdvanceQuote quote = review.Checkpoint?.Draft.Quote ?? review.SelectedQuote;
        panel.Add(NativeTheme.Title(quote.Name, 23));
        Add("id", quote.Identity.InternalId.ToString("D"));
        Add("rating", Format("Group {0} → {1} · cost rating {2} → {3} · {4} enabled member(s) · maximum {5}",
            quote.GroupRating, quote.TargetGroupRating, quote.CostRating, quote.TargetCostRating,
            quote.EnabledMemberCount, quote.RatingMaximum));
        Add("cost", Format("Cost {0} Karma · available {1} · after {2}", quote.KarmaCost,
            quote.AvailableKarma, quote.AvailableKarma - Math.Max(0, quote.KarmaCost)));
        Add("time", $"{quote.ApplicationDuration} · {quote.TimeAuthority}");
        // The shared quote exposes member count and exact-projection authority,
        // not a member list. Do not reconstruct membership from individual rows.
        foreach (CharacterCareerSkillGroupPrerequisiteResult prerequisite in quote.Prerequisites)
            Add($"prerequisite-{prerequisite.Prerequisite}",
                Format("Prerequisite · {0}", prerequisite.Prerequisite) + " · " + Format(
                    prerequisite.Satisfied ? "satisfied · {0}" : "blocked · {0}", prerequisite.Authority));
        Add("blocker", string.IsNullOrWhiteSpace(review.Blocker)
            ? Sr5CareerSkillGroupDraft.BlockerText(quote.Blocker) : review.Blocker);
        review.Status = NativeTheme.Body(review.Message, NativeTheme.Muted);
        review.Status.AutomationId = "tablet-career-skill-group-status";
        panel.Add(review.Status);
        bool reviewed = review.Checkpoint?.Phase == Sr5CareerCheckpointPhase.Reviewed;
        if (review.Draft is { } draft)
        {
            Add("expense", draft.Plan.ExpenseReason);
            Add("transaction", draft.Plan.TransactionId.ToString("D"));
            Add("date", draft.Plan.ExpenseDateLocal.ToString("O", CultureInfo.InvariantCulture));
        }
        if (review.Receipt is { } receipt)
        {
            Add("receipt", receipt.ReceiptDigest);
            Add("receipt-transaction", receipt.TransactionId.ToString("D"));
            Add("receipt-revision", checked(review.Checkpoint!.Draft.ExpectedContentRevision + 1).ToString(CultureInfo.InvariantCulture));
            Add("receipt-karma", $"{receipt.CharacterKarmaBefore} → {receipt.CharacterKarmaAfter}");
            Add("receipt-expense", $"{receipt.ExpenseAmount} · {receipt.ExpenseId:D}");
        }
        Action("review", Text("Review exact advancement"), ReviewTabletSkillGroupAsync,
            review.Draft is null && (review.Checkpoint is null || reviewed)
                && string.IsNullOrWhiteSpace(review.Blocker) && quote.CanAdvance);
        Action("apply", Text("Apply and verify once"), ApplyTabletSkillGroupAsync,
            review.Draft is not null && reviewed && review.Attempted == 0);
        Action("resolve", Text("Resolve interrupted apply"), ResolveTabletSkillGroupAsync,
            review.Checkpoint?.Phase is Sr5CareerCheckpointPhase.Applying or Sr5CareerCheckpointPhase.Applied);
        Action("abandon", Text("Abandon reviewed draft"), AbandonTabletSkillGroupAsync, reviewed);
        Action("acknowledge", Text("Acknowledge receipt"), AcknowledgeTabletSkillGroupAsync,
            review.Receipt is not null && review.Checkpoint?.Phase == Sr5CareerCheckpointPhase.Applied);

        void Add(string id, string value)
        {
            Label label = NativeTheme.Body(value, NativeTheme.Text);
            label.AutomationId = $"tablet-career-skill-group-{id}";
            panel.Add(label);
        }
        void Action(string id, string title, Func<TabletSkillGroupReview, Task<bool>> action, bool visible)
        {
            Button button = NativeTheme.SecondaryButton(title);
            button.AutomationId = $"tablet-career-skill-group-{id}";
            button.IsVisible = visible;
            button.IsEnabled = visible;
            button.Clicked += async (_, _) =>
            {
                if (!visible || !ReferenceEquals(button.Parent, panel) || !ReferenceEquals(_skillGroupReview, review)) return;
                await RunWithConditionalRefreshAsync(() => action(review));
            };
            panel.Add(button);
        }
    }

    private Task<bool> ReviewTabletSkillGroupAsync(TabletSkillGroupReview review)
    {
        if (!IsTabletSkillGroupContextCurrent(review, false) || review.InFlight != 0
            || !string.IsNullOrWhiteSpace(review.Blocker)) return Task.FromResult(false);
        string blocker;
        Sr5CareerSkillGroupDraft draft;
        if (review.Checkpoint is { } checkpoint)
        {
            if (!checkpoint.TryResume(review.Editor, out draft, out blocker)
                || !review.CheckpointAuthority.OwnsReviewed(checkpoint))
            {
                review.Status.Text = blocker;
                return Task.FromResult(false);
            }
        }
        else
        {
            if (!Sr5CareerSkillGroupDraft.TryCreate(review.Editor, review.SelectedQuote, review.OwnerId,
                    Guid.NewGuid(), DateTime.Now, out draft, out blocker)
                || !review.Store.TryCreate(Sr5CareerSkillGroupCheckpoint.FromDraft(draft),
                    out Sr5CareerSkillGroupCheckpoint stored, out blocker))
            {
                review.Status.Text = blocker;
                return Task.FromResult(false);
            }
            review.Checkpoint = stored;
        }
        review.Draft = draft;
        review.Attempted = 0;
        RenderTabletSkillGroupReview(review);
        return Task.FromResult(false);
    }

    private async Task<bool> ApplyTabletSkillGroupAsync(TabletSkillGroupReview review)
    {
        if (Interlocked.CompareExchange(ref review.InFlight, 1, 0) != 0) return false;
        try
        {
            if (review.Attempted != 0 || review.Draft is not { } draft || review.Checkpoint is not { } checkpoint
                || !IsTabletSkillGroupContextCurrent(review, false) || !review.CheckpointAuthority.OwnsReviewed(checkpoint)) return false;
            if (!await _confirm(Text("Advance skill group"),
                    Format("{0} · {1} → {2} · {3} Karma", draft.Quote.Name, draft.Quote.GroupRating,
                        draft.Quote.TargetGroupRating, draft.Quote.KarmaCost), Text("Apply and verify once"), Text("Cancel"))) return false;
            if (!IsTabletSkillGroupContextCurrent(review, false) || !review.CheckpointAuthority.OwnsReviewed(checkpoint)) return false;
            if (!review.Store.TryBeginApply(Sr5CareerSkillGroupCheckpointCas.From(checkpoint),
                    out Sr5CareerSkillGroupCheckpoint applying, out string blocker))
            {
                review.Status.Text = blocker;
                return false;
            }
            review.Attempted = 1;
            review.Checkpoint = applying;
            review.Message = Text("An interrupted apply is locked and will be resolved only by the exact idempotent Core command.");
            RenderTabletSkillGroupReview(review);
            Sr5CareerSkillGroupApplyResult result = await review.Authority.ApplyAsync(draft, applying, review.Store, review.Token);
            if (IsTabletSkillGroupContextCurrent(review, true)) RecordTabletSkillGroupResolution(review, result.Resolution);
            return false;
        }
        finally { Interlocked.Exchange(ref review.InFlight, 0); }
    }

    private async Task<bool> ResolveTabletSkillGroupAsync(TabletSkillGroupReview review)
    {
        if (Interlocked.CompareExchange(ref review.InFlight, 1, 0) != 0) return false;
        try
        {
            if (review.Checkpoint is not { } checkpoint || checkpoint.Phase == Sr5CareerCheckpointPhase.Reviewed
                || !IsTabletSkillGroupContextCurrent(review, true)
                || !review.CheckpointAuthority.OwnsCurrentRunner(checkpoint)) return false;
            review.Resolving = true;
            Sr5CareerSkillGroupRecoveryResolution resolution = await review.Authority.ResolveAsync(checkpoint, review.Store, review.Token);
            if (IsTabletSkillGroupContextCurrent(review, true)) RecordTabletSkillGroupResolution(review, resolution);
            return false;
        }
        finally
        {
            review.Resolving = false;
            Interlocked.Exchange(ref review.InFlight, 0);
        }
    }

    private void RecordTabletSkillGroupResolution(TabletSkillGroupReview review, Sr5CareerSkillGroupRecoveryResolution resolution)
    {
        if (resolution.Status == Sr5CareerSkillGroupRecoveryStatus.OutcomeUnknown)
        {
            review.Message = Text("An interrupted apply is locked and will be resolved only by the exact idempotent Core command.");
        }
        else
        {
            if (review.Checkpoint!.Phase == Sr5CareerCheckpointPhase.Applying)
            {
                if (!review.Store.TryRecordAuthoritativeResolution(Sr5CareerSkillGroupCheckpointCas.From(review.Checkpoint),
                        resolution, out Sr5CareerSkillGroupCheckpoint stored, out string blocker))
                {
                    review.Status.Text = blocker;
                    return;
                }
                review.Checkpoint = stored;
            }
            review.Receipt = resolution.Receipt;
            review.Message = resolution.Status == Sr5CareerSkillGroupRecoveryStatus.AppliedVerified
                ? Text("Verified saved advancement")
                : Text("Core rejected the exact command and the runner stayed at the reviewed revision. Return and resume the review before retrying.");
            // A verified rejection requires an explicit review before another Apply.
            if (resolution.Status == Sr5CareerSkillGroupRecoveryStatus.NotAppliedVerified) review.Draft = null;
        }
        RenderTabletSkillGroupReview(review);
    }

    private async Task<bool> AbandonTabletSkillGroupAsync(TabletSkillGroupReview review)
    {
        if (Interlocked.CompareExchange(ref review.InFlight, 1, 0) != 0) return false;
        try
        {
            if (review.Checkpoint is not { } checkpoint || !IsTabletSkillGroupContextCurrent(review, false)
                || !review.CheckpointAuthority.OwnsReviewed(checkpoint)) return false;
            if (!await _confirm(Text("Abandon reviewed draft?"),
                    Text("This removes only the durable review checkpoint and does not change the runner."), Text("Abandon"), Text("Keep"))) return false;
            if (!IsTabletSkillGroupContextCurrent(review, false) || !review.CheckpointAuthority.OwnsReviewed(checkpoint)) return false;
            if (!review.Store.TryDeleteReviewed(Sr5CareerSkillGroupCheckpointCas.From(checkpoint), out string blocker))
                review.Status.Text = blocker;
            else
            {
                review.Checkpoint = null;
                review.Draft = null;
                review.Message = string.Empty;
                RenderTabletSkillGroupReview(review);
            }
            return false;
        }
        finally { Interlocked.Exchange(ref review.InFlight, 0); }
    }

    private Task<bool> AcknowledgeTabletSkillGroupAsync(TabletSkillGroupReview review)
    {
        if (review.InFlight != 0 || !IsTabletSkillGroupContextCurrent(review, true)
            || review.Checkpoint is not { } checkpoint || review.Receipt is not { } receipt
            || !review.CheckpointAuthority.OwnsCurrentRunner(checkpoint)) return Task.FromResult(false);
        if (!review.Store.TryDeleteApplied(Sr5CareerSkillGroupCheckpointCas.From(checkpoint), receipt, out string blocker))
        {
            review.Status.Text = blocker;
            return Task.FromResult(false);
        }
        _skillGroupEditor = null;
        _skillGroupExpected = null;
        _selectedSkillGroup = null;
        return Task.FromResult(true);
    }

    private sealed class TabletSkillGroupReview(CharacterOverviewState expected, long generation,
        CareerSkillGroupAdvanceEditorState editor, CharacterCareerSkillGroupAdvanceQuote selectedQuote,
        string selectedDigest, Guid ownerId, CancellationToken token)
    {
        public CharacterOverviewState Expected { get; } = expected;
        public long Generation { get; } = generation;
        public CareerSkillGroupAdvanceEditorState Editor { get; } = editor;
        public CharacterCareerSkillGroupAdvanceQuote SelectedQuote { get; } = selectedQuote;
        public string SelectedDigest { get; } = selectedDigest;
        public Guid OwnerId { get; } = ownerId;
        public CancellationToken Token { get; } = token;
        public Sr5CareerSkillGroupDraft? Draft { get; set; }
        public Sr5CareerSkillGroupCheckpoint? Checkpoint { get; set; }
        public Sr5CareerSkillGroupCheckpointStore Store { get; set; } = null!;
        public Sr5CareerSkillGroupCoordinator Authority { get; set; } = null!;
        public ISr5CareerSkillGroupCheckpointAuthority CheckpointAuthority { get; set; } = null!;
        public CharacterCareerSkillGroupAdvanceReceipt? Receipt { get; set; }
        public VerticalStackLayout Panel { get; } = new() { Spacing = 8, AutomationId = "tablet-career-skill-group-panel" };
        public Label Status { get; set; } = null!;
        public string Blocker { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public bool Resolving;
        public int Attempted;
        public int InFlight;
    }

    private sealed class TabletSkillGroupCheckpointAuthority(ISr5CareerSkillGroupCheckpointAuthority inner,
        Func<bool> exact, Func<bool> successor) : ISr5CareerSkillGroupCheckpointAuthority
    {
        public Guid CurrentOwnerId => inner.CurrentOwnerId;
        public bool OwnsReviewed(Sr5CareerSkillGroupCheckpoint checkpoint) => exact() && inner.OwnsReviewed(checkpoint);
        public bool OwnsCurrentRunner(Sr5CareerSkillGroupCheckpoint checkpoint) => successor() && inner.OwnsCurrentRunner(checkpoint);
        public bool OwnsResolution(Sr5CareerSkillGroupCheckpoint checkpoint, Sr5CareerSkillGroupRecoveryStatus status)
            => successor() && inner.OwnsResolution(checkpoint, status);
        public bool OwnsCorrected(Sr5CareerSkillGroupCheckpoint checkpoint, CharacterCareerSkillGroupAdvanceReceipt receipt,
            CharacterCareerSkillGroupCorrectionPlan correction) => false;
    }

    private sealed class TabletSkillGroupPresenter(TabletBuildPage page, TabletSkillGroupReview review,
        ISr5CareerSkillGroupPresenter inner) : ISr5CareerSkillGroupPresenter
    {
        public Sr5CareerRunnerBinding Binding => inner.Binding;
        public Task<CareerSkillGroupAdvanceEditorState?> LoadSkillGroupsAsync(CancellationToken cancellationToken)
            => inner.LoadSkillGroupsAsync(cancellationToken);
        public async Task<CharacterCareerSkillGroupAdvanceResult?> AdvanceAsync(
            CharacterCareerSkillGroupAdvanceCommand command, CancellationToken cancellationToken)
        {
            review.Token.ThrowIfCancellationRequested();
            if (!page.IsTabletSkillGroupContextCurrent(review, true)
                || review.Checkpoint is not { } checkpoint || command != checkpoint.Draft.ToCommand())
                throw new OperationCanceledException(review.Token);
            return await page._skillGroupDispatch(() => page.Coordinator.TryAdvanceBoundCareerSkillGroupAsync(
                command, review.Expected, () => page.IsTabletSkillGroupContextCurrent(review, true),
                review.Resolving, cancellationToken)).ConfigureAwait(false);
        }
    }
}
