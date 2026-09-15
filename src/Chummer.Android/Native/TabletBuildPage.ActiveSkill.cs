using System.Globalization;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;
using static Chummer.Android.Native.Sr5CareerFlowStrings;

namespace Chummer.Android.Native;

public sealed partial class TabletBuildPage
{
    private readonly Func<ISr5CareerReviewedCheckpointAuthority, Sr5CareerDraftCheckpointStore> _activeSkillStoreFactory;
    private readonly Func<Func<Task<bool>>, Task<bool>> _activeSkillDispatch;
    private ISr5CareerCheckpointOwnerAuthority? _activeSkillOwner;
    private CancellationTokenSource? _activeSkillOperation;
    private TabletActiveSkillReview? _activeSkillReview;

    private void InvalidateTabletActiveSkillReview()
    {
        _activeSkillReview = null;
        CancellationTokenSource? operation = _activeSkillOperation;
        _activeSkillOperation = null;
        operation?.Cancel();
        operation?.Dispose();
        // Never erase a durable Applying checkpoint on selection change or departure.
    }

    private void AddTabletActiveSkillInspector(WorkspaceCollectionItemEditorState item,
        CharacterOverviewState expected, long generation)
    {
        if (expected.ActiveCollectionEditor?.Kind != WorkspaceCollectionKind.Skill
            || item.Target.NestedItemId is not null
            || !Guid.TryParse(item.Target.ItemId, out Guid skillId) || skillId == Guid.Empty
            || !Sr5CareerWizardCatalog.IsSr5CareerRunner(expected.Profile?.Created == true, expected.Rules?.GameEdition))
            return;

        VerticalStackLayout panel = new() { Spacing = 8, AutomationId = "tablet-career-active-skill-panel" };
        Button review = NativeTheme.SecondaryButton(Text("Review advancement"));
        review.AutomationId = "tablet-career-active-skill-review";
        review.Clicked += async (_, _) =>
        {
            if (!ReferenceEquals(review.Parent, panel) || !IsCurrentInspector(generation, expected, item.Target)) return;
            await RunWithConditionalRefreshAsync(() => LoadTabletActiveSkillReviewAsync(item, expected, generation, panel));
        };
        panel.Add(review);
        _inspector.Add(panel);
    }

    private async Task<bool> LoadTabletActiveSkillReviewAsync(WorkspaceCollectionItemEditorState item,
        CharacterOverviewState expected, long generation, VerticalStackLayout panel)
    {
        if (_activeSkillOperation is not null || !IsCurrentInspector(generation, expected, item.Target)
            || !Coordinator.IsTabletCareerActiveSkillOwnerCurrent(expected)) return false;
        CancellationTokenSource operation = new();
        _activeSkillOperation = operation;
        CancellationToken token = operation.Token;
        bool established = false;
        try
        {
            established = await EstablishTabletActiveSkillReviewAsync(item, expected, generation, panel, token);
            return false;
        }
        finally
        {
            // An unavailable quote/read must leave the same panel retryable. Do
            // not retire a newer operation after selection changed during await,
            // and never remove a durable checkpoint (especially Applying).
            if (!established && ReferenceEquals(_activeSkillOperation, operation))
            {
                _activeSkillReview = null;
                _activeSkillOperation = null;
                operation.Cancel();
                operation.Dispose();
            }
        }
    }

    private async Task<bool> EstablishTabletActiveSkillReviewAsync(WorkspaceCollectionItemEditorState item,
        CharacterOverviewState expected, long generation, VerticalStackLayout panel, CancellationToken token)
    {
        RunnerSessionSr5CareerActiveSkillPresenter presenter = new(Coordinator);
        ISr5CareerCheckpointOwnerAuthority owner = _activeSkillOwner ??= new PreferencesSr5CareerCheckpointOwnerAuthority();
        Guid ownerId = owner.CurrentOwnerId;
        CareerActiveSkillAdvanceEditorState? editor = await new Sr5CareerActiveSkillCoordinator(presenter, owner).PrepareAsync(token);
        if (token.IsCancellationRequested || !IsCurrentInspector(generation, expected, item.Target)
            || !Coordinator.IsTabletCareerActiveSkillOwnerCurrent(expected)
            || owner.CurrentOwnerId != ownerId || editor is null
            || editor.WorkspaceId != expected.WorkspaceId || editor.ContentRevision != expected.ContentRevision) return false;

        CharacterCareerActiveSkillAdvanceQuote[] matches = editor.Skills
            .Where(quote => quote.Identity.SkillId.ToString("D").Equals(item.Target.ItemId, StringComparison.OrdinalIgnoreCase))
            .Take(2).ToArray();
        if (matches.Length != 1 || !CharacterCareerActiveSkillAdvanceRules.IsCoherent(matches[0]))
        {
            panel.Add(NativeTheme.Body(Text("No exact active-skill quote is available."), NativeTheme.Danger));
            return false;
        }

        TabletActiveSkillReview review = new(expected, generation, item.Target, editor, matches[0], ownerId, token);
        Sr5CareerLiveReviewedCheckpointAuthority live = new(owner, editor, () => presenter.Binding);
        review.ReviewedAuthority = new TabletActiveSkillReviewedAuthority(live,
            () => IsTabletActiveSkillContextCurrent(review, allowSuccessor: false),
            () => IsTabletActiveSkillContextCurrent(review, allowSuccessor: true));
        review.Store = _activeSkillStoreFactory(review.ReviewedAuthority);
        review.Authority = new Sr5CareerActiveSkillCoordinator(new TabletActiveSkillPresenter(this, review, presenter), owner);
        _activeSkillReview = review;
        if (review.Store.TryRead(out Sr5CareerDraftCheckpoint existing, out string blocker))
        {
            if (existing.SkillId != review.Quote.Identity.SkillId || existing.SourceSkillId != review.Quote.Identity.SourceSkillId
                || !review.ReviewedAuthority.OwnsCurrentRunner(existing))
            {
                panel.Add(NativeTheme.Body(Text("This recovery lock belongs to another local owner or SR5 runner context."), NativeTheme.Danger));
                return false;
            }
            review.Checkpoint = existing;
            if (existing.Phase == Sr5CareerCheckpointPhase.Reviewed)
            {
                if (!existing.TryResume(editor, out Sr5CareerActiveSkillDraft resumed, out blocker)
                    || !review.ReviewedAuthority.Owns(existing))
                {
                    panel.Add(NativeTheme.Body(Text("The saved review no longer owns this exact revision."), NativeTheme.Danger));
                    return false;
                }
                review.Draft = resumed;
            }
        }
        else if (!string.IsNullOrWhiteSpace(blocker))
        {
            panel.Add(NativeTheme.Body(blocker, NativeTheme.Danger));
            return false;
        }
        else
        {
            if (!Sr5CareerActiveSkillDraft.TryCreate(editor, review.Quote, ownerId, Guid.NewGuid(), DateTime.Now,
                    out Sr5CareerActiveSkillDraft draft, out blocker)
                || !review.Store.TryCreate(Sr5CareerDraftCheckpoint.FromDraft(draft), out Sr5CareerDraftCheckpoint stored, out blocker))
            {
                panel.Add(NativeTheme.Body(blocker, NativeTheme.Danger));
                return false;
            }
            review.Draft = draft;
            review.Checkpoint = stored;
        }
        RenderTabletActiveSkillReview(review, panel);
        return true;
    }

    private bool IsTabletActiveSkillContextCurrent(TabletActiveSkillReview review, bool allowSuccessor)
    {
        if (!allowSuccessor && !IsCurrentInspector(review.Generation, review.Expected, review.Target)) return false;
        CharacterOverviewState current = Coordinator.State;
        CharacterOverviewState expected = review.Expected;
        bool revision = current.ContentRevision == expected.ContentRevision
            || allowSuccessor && expected.ContentRevision < long.MaxValue && current.ContentRevision == expected.ContentRevision + 1;
        return ReferenceEquals(_activeSkillReview, review) && !review.Token.IsCancellationRequested && !_departed
            && Coordinator.IsTabletCareerActiveSkillOwnerCurrent(expected)
            && Volatile.Read(ref _inspectorGeneration) == review.Generation
            && _selectedTarget is not null && CollectionItemEditorPage.TargetsMatch(_selectedTarget, review.Target)
            && _activeSkillOwner?.CurrentOwnerId == review.OwnerId
            && current.WorkspaceId == expected.WorkspaceId && current.DisplayOwnerContext == expected.DisplayOwnerContext
            && string.Equals(current.ActiveSectionId, expected.ActiveSectionId, StringComparison.Ordinal)
            && Sr5CareerWizardCatalog.IsSr5CareerRunner(current.Profile?.Created == true, current.Rules?.GameEdition)
            && revision && current.SavedRevision == current.ContentRevision && !current.IsDirty && current.Error is null
            && current.ActiveCollectionEditor?.Items.Count(item => CollectionItemEditorPage.TargetsMatch(item.Target, review.Target)) == 1;
    }

    private void RenderTabletActiveSkillReview(TabletActiveSkillReview review, VerticalStackLayout panel)
    {
        review.Panel = panel;
        panel.Clear();
        panel.Add(NativeTheme.Eyebrow(Text("Review exact diff")));
        Add("tablet-career-active-skill-rating", Format("Current {0} · after {1} · maximum {2}",
            review.Quote.TotalBaseRating, review.Quote.TotalBaseRating + 1, review.Quote.RatingMaximum));
        Add("tablet-career-active-skill-cost", Format("Cost {0} Karma · available {1} · after {2}",
            review.Quote.KarmaCost, review.Quote.AvailableKarma, review.Quote.AvailableKarma - review.Quote.KarmaCost));
        Add("tablet-career-active-skill-skill-id", review.Quote.Identity.SkillId.ToString("D"));
        Add("tablet-career-active-skill-source-id", review.Quote.Identity.SourceSkillId.ToString("D"));
        Label status = NativeTheme.Body(string.Empty, NativeTheme.Muted);
        status.AutomationId = "tablet-career-active-skill-status";
        review.Status = status;
        panel.Add(status);
        if (review.Draft is { } draft)
        {
            panel.Add(NativeTheme.Metric(Text("Expense"), draft.Plan.ExpenseReason));
            panel.Add(NativeTheme.Metric(Text("Date"), draft.Plan.ExpenseDateLocal.ToString("O", CultureInfo.InvariantCulture)));
            Button apply = NativeTheme.PrimaryButton(Text("Apply and verify once"));
            apply.AutomationId = "tablet-career-active-skill-apply";
            apply.Clicked += async (_, _) =>
            {
                if (!ReferenceEquals(apply.Parent, panel) || !IsTabletActiveSkillContextCurrent(review, false)
                    || Interlocked.CompareExchange(ref review.Attempted, 1, 0) != 0) return;
                apply.IsEnabled = false;
                await RunWithConditionalRefreshAsync(async () =>
                {
                    await ApplyTabletActiveSkillAsync(review, status);
                    return false;
                });
            };
            panel.Add(apply);
        }
        Button abandon = NativeTheme.SecondaryButton(Text("Abandon reviewed draft"));
        abandon.AutomationId = "tablet-career-active-skill-abandon";
        abandon.IsVisible = review.Checkpoint?.Phase == Sr5CareerCheckpointPhase.Reviewed;
        abandon.Clicked += async (_, _) =>
        {
            if (!ReferenceEquals(abandon.Parent, panel) || !IsTabletActiveSkillContextCurrent(review, false)) return;
            await RunWithConditionalRefreshAsync(async () =>
            {
                if (!await _confirm(Text("Abandon reviewed draft?"),
                        Text("This removes only the durable review checkpoint and does not change the runner."), Text("Abandon"), Text("Keep"))) return false;
                if (!IsTabletActiveSkillContextCurrent(review, false) || !review.ReviewedAuthority.Owns(review.Checkpoint!)) return false;
                if (review.Store.TryDeleteReviewed(Sr5CareerCheckpointCas.From(review.Checkpoint!), out string blocker)) return true;
                status.Text = blocker;
                return false;
            });
        };
        panel.Add(abandon);
        Button resolve = NativeTheme.SecondaryButton(Text("Resolve interrupted apply"));
        resolve.AutomationId = "tablet-career-active-skill-resolve";
        resolve.IsVisible = review.Checkpoint?.Phase is Sr5CareerCheckpointPhase.Applying or Sr5CareerCheckpointPhase.Applied;
        review.ResolveButton = resolve;
        resolve.Clicked += async (_, _) =>
        {
            if (!ReferenceEquals(resolve.Parent, panel) || !IsTabletActiveSkillContextCurrent(review, true)) return;
            await RunWithConditionalRefreshAsync(async () =>
            {
                await ResolveTabletActiveSkillAsync(review, status);
                return false;
            });
        };
        panel.Add(resolve);
        Button acknowledge = NativeTheme.PrimaryButton(Text("Acknowledge receipt"));
        acknowledge.AutomationId = "tablet-career-active-skill-acknowledge";
        acknowledge.IsVisible = false;
        review.AcknowledgeButton = acknowledge;
        acknowledge.Clicked += async (_, _) =>
        {
            if (!ReferenceEquals(acknowledge.Parent, panel) || !IsTabletActiveSkillContextCurrent(review, true)) return;
            await RunWithConditionalRefreshAsync(() =>
            {
                if (review.Receipt is null || !review.ReviewedAuthority.OwnsCurrentRunner(review.Checkpoint!)) return Task.FromResult(false);
                bool removed = review.Store.TryDeleteApplied(Sr5CareerCheckpointCas.From(review.Checkpoint!), review.Receipt, out string blocker);
                if (!removed) status.Text = blocker;
                return Task.FromResult(removed);
            });
        };
        panel.Add(acknowledge);

        void Add(string id, string value)
        {
            Label label = NativeTheme.Body(value, NativeTheme.Text);
            label.AutomationId = id;
            panel.Add(label);
        }
    }

    private async Task ApplyTabletActiveSkillAsync(TabletActiveSkillReview review, Label status)
    {
        if (review.Draft is not { } draft || review.Checkpoint is not { } checkpoint
            || !IsTabletActiveSkillContextCurrent(review, false) || !review.ReviewedAuthority.Owns(checkpoint)) return;
        if (!review.Store.TryBeginApply(Sr5CareerCheckpointCas.From(checkpoint), out Sr5CareerDraftCheckpoint applying, out string blocker))
        {
            status.Text = blocker;
            return;
        }
        review.Checkpoint = applying;
        review.ResolveButton!.IsVisible = true;
        status.Text = Text("The one-shot apply is in progress or awaiting authoritative recovery.");
        Sr5CareerApplyResult result = await review.Authority.ApplyAsync(draft, applying, review.Store, review.Token);
        if (!IsTabletActiveSkillContextCurrent(review, true)) return;
        RecordTabletActiveSkillResolution(review, result.Resolution, status);
    }

    private async Task ResolveTabletActiveSkillAsync(TabletActiveSkillReview review, Label status)
    {
        if (review.Checkpoint is not { } checkpoint || checkpoint.Phase == Sr5CareerCheckpointPhase.Reviewed
            || !review.ReviewedAuthority.OwnsCurrentRunner(checkpoint)) return;
        Sr5CareerRecoveryResolution resolution = await review.Authority.ResolveAsync(checkpoint, review.Token);
        if (!IsTabletActiveSkillContextCurrent(review, true)) return;
        RecordTabletActiveSkillResolution(review, resolution, status);
    }

    private void RecordTabletActiveSkillResolution(TabletActiveSkillReview review,
        Sr5CareerRecoveryResolution resolution, Label status)
    {
        Sr5CareerDraftCheckpoint checkpoint = review.Checkpoint
            ?? throw new InvalidOperationException(Text("The review does not own the durable Career checkpoint."));
        if (resolution.Status == Sr5CareerRecoveryStatus.OutcomeUnknown)
        {
            status.Text = Text("The one-shot apply is in progress or awaiting authoritative recovery.");
            return;
        }
        if (checkpoint.Phase == Sr5CareerCheckpointPhase.Applying)
        {
            if (!review.Store.TryRecordAuthoritativeResolution(Sr5CareerCheckpointCas.From(checkpoint), resolution,
                    out Sr5CareerDraftCheckpoint stored, out string blocker))
            {
                status.Text = blocker;
                return;
            }
            review.Checkpoint = stored;
        }
        status.Text = resolution.Status == Sr5CareerRecoveryStatus.AppliedVerified
            ? Text("The interrupted apply was found in fresh typed projections.")
            : Text("Fresh typed projections prove the action was not saved. The reviewed draft may now be resumed.");
        review.Receipt = resolution.Receipt;
        review.AcknowledgeButton!.IsVisible = resolution.Status == Sr5CareerRecoveryStatus.AppliedVerified && resolution.Receipt is not null;
        if (resolution.Status == Sr5CareerRecoveryStatus.NotAppliedVerified
            && review.Checkpoint?.Phase == Sr5CareerCheckpointPhase.Reviewed
            && review.Checkpoint.TryResume(review.Editor, out Sr5CareerActiveSkillDraft resumed, out _)
            && review.ReviewedAuthority.Owns(review.Checkpoint)
            && review.Panel is { } panel)
        {
            // Only authenticated fresh no-mutation proof can restore an explicit
            // Apply. Unknown/Applied outcomes never reset one-shot admission.
            review.Draft = resumed;
            Interlocked.Exchange(ref review.Attempted, 0);
            RenderTabletActiveSkillReview(review, panel);
            review.Status!.Text = Text("Fresh typed projections prove the action was not saved. The reviewed draft may now be resumed.");
        }
    }

    private sealed class TabletActiveSkillReview(CharacterOverviewState expected, long generation,
        WorkspaceCollectionItemTarget target, CareerActiveSkillAdvanceEditorState editor,
        CharacterCareerActiveSkillAdvanceQuote quote, Guid ownerId, CancellationToken token)
    {
        public CharacterOverviewState Expected { get; } = expected;
        public long Generation { get; } = generation;
        public WorkspaceCollectionItemTarget Target { get; } = target;
        public CareerActiveSkillAdvanceEditorState Editor { get; } = editor;
        public CharacterCareerActiveSkillAdvanceQuote Quote { get; } = quote;
        public Guid OwnerId { get; } = ownerId;
        public CancellationToken Token { get; } = token;
        public Sr5CareerActiveSkillDraft? Draft { get; set; }
        public Sr5CareerDraftCheckpoint? Checkpoint { get; set; }
        public Sr5CareerActiveSkillCoordinator Authority { get; set; } = null!;
        public Sr5CareerDraftCheckpointStore Store { get; set; } = null!;
        public ISr5CareerReviewedCheckpointAuthority ReviewedAuthority { get; set; } = null!;
        public Sr5CareerActiveSkillReceipt? Receipt { get; set; }
        public Button? ResolveButton { get; set; }
        public Button? AcknowledgeButton { get; set; }
        public VerticalStackLayout? Panel { get; set; }
        public Label? Status { get; set; }
        public int Attempted;
    }

    private sealed class TabletActiveSkillReviewedAuthority(ISr5CareerReviewedCheckpointAuthority inner,
        Func<bool> exact, Func<bool> successor) : ISr5CareerReviewedCheckpointAuthority
    {
        public Guid CurrentOwnerId => inner.CurrentOwnerId;
        public bool Owns(Sr5CareerDraftCheckpoint checkpoint) => exact() && inner.Owns(checkpoint);
        public bool OwnsCurrentRunner(Sr5CareerDraftCheckpoint checkpoint) => successor() && inner.OwnsCurrentRunner(checkpoint);
    }

    private sealed class TabletActiveSkillPresenter(TabletBuildPage page, TabletActiveSkillReview review,
        ISr5CareerActiveSkillPresenter inner) : ISr5CareerActiveSkillPresenter
    {
        public Sr5CareerRunnerBinding Binding => inner.Binding;
        private void RequireCurrent()
        {
            review.Token.ThrowIfCancellationRequested();
            if (!page.IsTabletActiveSkillContextCurrent(review, true)) throw new OperationCanceledException(review.Token);
        }
        public async Task<CareerActiveSkillAdvanceEditorState?> LoadActiveSkillsAsync(CancellationToken token)
        {
            RequireCurrent();
            CareerActiveSkillAdvanceEditorState? result = await inner.LoadActiveSkillsAsync(token).ConfigureAwait(false);
            RequireCurrent();
            return result;
        }
        public async Task<CareerKarmaExpenseEditorState?> LoadKarmaExpensesAsync(CancellationToken token)
        {
            RequireCurrent();
            CareerKarmaExpenseEditorState? result = await inner.LoadKarmaExpensesAsync(token).ConfigureAwait(false);
            RequireCurrent();
            return result;
        }
        public async Task<bool> ApplyAndSaveAsync(CareerActiveSkillAdvanceRequest request, CancellationToken token)
        {
            RequireCurrent();
            bool result = await page._activeSkillDispatch(() => page.Coordinator.TryApplyBoundCareerActiveSkillAdvanceAsync(
                request, review.Expected, () => page.IsTabletActiveSkillContextCurrent(review, false), token)).ConfigureAwait(false);
            RequireCurrent();
            return result;
        }
    }
}
