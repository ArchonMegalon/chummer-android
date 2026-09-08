using System.Globalization;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;

namespace Chummer.Android.Native;

public enum Sr5AfterRunRewardPhoneStatus
{
    Loading,
    Editing,
    Review,
    InvalidInput,
    ReviewUnavailable,
    Pending,
    OutcomeUnknown,
    Recorded,
    RefreshRequired,
    RunnerChanged,
    JournalUnavailable
}

/// <summary>Editable local intent only. IDs belong to the model, never text fields.</summary>
public sealed record Sr5AfterRunRewardPhoneDraft(
    string Karma, string Nuyen, string Reason, DateTime Date, TimeSpan Time, bool NoAward);

/// <summary>
/// UI-thread-owned reward workflow. Core and journal work stays behind the real
/// coordinator's background boundary. It never retries a mutation on appearance,
/// creates a replacement operation for a pending command, or treats cancellation
/// as rollback. A page must retain this model for its lifetime.
/// </summary>
public sealed class Sr5AfterRunRewardPhoneModel
{
    private readonly Sr5AfterRunRewardCoordinator _coordinator;
    private readonly ISr5AfterRunRewardRunnerAuthority _authority;
    private readonly ISr5AfterRunRewardSavedRunnerRefresh _refresh;
    private readonly Sr5AfterRunRewardRunnerBinding _selection;
    private bool _initialized;
    private bool _busy;
    private CharacterAfterRunRewardCommand? _confirmedIntent;

    public Sr5AfterRunRewardPhoneModel(Sr5AfterRunRewardCoordinator coordinator,
        ISr5AfterRunRewardRunnerAuthority authority, ISr5AfterRunRewardSavedRunnerRefresh refresh,
        DateTime localNow)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _authority = authority ?? throw new ArgumentNullException(nameof(authority));
        _refresh = refresh ?? throw new ArgumentNullException(nameof(refresh));
        _selection = authority.Current;
        OperationId = Guid.NewGuid();
        RewardId = Guid.NewGuid();
        Draft = new("0", "0", string.Empty, localNow.Date,
            new TimeSpan(localNow.Hour, localNow.Minute, localNow.Second), false);
    }

    public Guid OperationId { get; private set; }
    public Guid RewardId { get; private set; }
    public Sr5AfterRunRewardPhoneDraft Draft { get; private set; }
    public Sr5AfterRunRewardPhoneStatus Status { get; private set; } = Sr5AfterRunRewardPhoneStatus.Loading;
    public Sr5AfterRunRewardReview? Review { get; private set; }
    public Sr5AfterRunRewardCheckpoint? Checkpoint { get; private set; }
    public Sr5AfterRunRewardConsequencesHandoff? Handoff { get; private set; }
    public IReadOnlyList<Sr5AfterRunRewardCheckpoint> RecordedRewards { get; private set; } = [];
    public bool IsBusy => _busy;
    public bool HasCurrentSelection => OwnsReloadableSelection();
    public bool HasRetainedIntent => _confirmedIntent is not null;
    public bool CanEdit => _initialized && !_busy && !HasRetainedIntent && OwnsSelection()
        && Status != Sr5AfterRunRewardPhoneStatus.JournalUnavailable;
    public bool CanConfirm => CanEdit && Review is { } review
        && review.Runner == _authority.Current && Status == Sr5AfterRunRewardPhoneStatus.Review;
    public bool CanRecover => !_busy && HasRetainedIntent && OwnsReloadableSelection();
    public bool CanRetry => CanRecover && OwnsSelection()
        && Checkpoint?.Phase != Sr5AfterRunRewardCheckpointPhase.Applied;
    public bool CanContinue => !_busy && Handoff?.IsCurrent(_authority) == true;

    public bool UpdateDraft(Sr5AfterRunRewardPhoneDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (!CanEdit) return false;
        if (Draft == draft) return true;
        Draft = draft;
        Review = null;
        Handoff = null;
        Status = Sr5AfterRunRewardPhoneStatus.Editing;
        return true;
    }

    /// <summary>Loads pending identity only. No Commit, automatic retry or implicit confirmation.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_busy) return;
        if (!OwnsSelection()) { ChangedRunner(); return; }
        if (_initialized && HasRetainedIntent) return;
        _busy = true;
        try
        {
            var state = await _coordinator.ReadEntryStateAsync(cancellationToken);
            if (!OwnsSelection()) { ChangedRunner(); return; }
            RecordedRewards = state.Recorded;
            _initialized = true;
            if (state.RecoveryRequired is { } pending)
            {
                Retain(pending);
                Status = Sr5AfterRunRewardPhoneStatus.Pending;
            }
            else if (!HasRetainedIntent)
                Status = Review is null ? Sr5AfterRunRewardPhoneStatus.Editing : Sr5AfterRunRewardPhoneStatus.Review;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception) { EntryUnavailable(); }
        finally { _busy = false; }
    }

    /// <summary>
    /// Explicitly resume a recorded reward by identity. Re-read ownership first,
    /// then verify through Core Lookup and refresh. No Commit or new IDs. A
    /// different pending operation always takes priority over history selection.
    /// </summary>
    public async Task ResumeRecordedRewardAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        if (!CanEdit || cancellationToken.IsCancellationRequested) return;
        _busy = true;
        Review = null;
        Handoff = null;
        try
        {
            var state = await _coordinator.ReadEntryStateAsync(cancellationToken);
            if (!OwnsSelection()) { ChangedRunner(); return; }
            RecordedRewards = state.Recorded;
            if (state.RecoveryRequired is { } pending)
            {
                Retain(pending);
                Status = Sr5AfterRunRewardPhoneStatus.Pending;
                return;
            }
            var selected = state.Recorded.SingleOrDefault(entry => entry.Command.OperationId == operationId);
            if (selected is null)
            {
                Status = Sr5AfterRunRewardPhoneStatus.Editing;
                return;
            }
            Retain(selected);
            Status = Sr5AfterRunRewardPhoneStatus.Pending;
            var result = await _coordinator.RecoverAsync(operationId, cancellationToken);
            await AcceptAsync(result, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Status = HasRetainedIntent ? Sr5AfterRunRewardPhoneStatus.Pending : Sr5AfterRunRewardPhoneStatus.Editing;
        }
        catch (Exception)
        {
            if (HasRetainedIntent) Status = Sr5AfterRunRewardPhoneStatus.OutcomeUnknown;
            else EntryUnavailable();
        }
        finally { _busy = false; }
    }

    public async Task PreviewAsync(CultureInfo? culture = null, CancellationToken cancellationToken = default)
    {
        if (!CanEdit) return;
        Review = null;
        Handoff = null;
        if (!TryRequest(culture ?? CultureInfo.CurrentCulture, out var request))
        {
            Status = Sr5AfterRunRewardPhoneStatus.InvalidInput;
            return;
        }
        _busy = true;
        try
        {
            var result = await _coordinator.PreviewAsync(request!, cancellationToken);
            if (!OwnsSelection()) { ChangedRunner(); return; }
            Review = result.Review;
            Status = result.Outcome == CharacterAfterRunRewardOutcome.Available && Review is not null
                ? Sr5AfterRunRewardPhoneStatus.Review : Sr5AfterRunRewardPhoneStatus.ReviewUnavailable;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { Status = Sr5AfterRunRewardPhoneStatus.Editing; }
        catch (Exception) { Status = Sr5AfterRunRewardPhoneStatus.ReviewUnavailable; }
        finally { _busy = false; }
    }

    /// <summary>Only called by the explicit confirm button for the displayed Core review.</summary>
    public async Task ConfirmAsync(CancellationToken cancellationToken = default)
    {
        // No coordinator call and no durable intent exist yet. Preserve the
        // displayed review when the page lifetime has already been canceled.
        if (!CanConfirm || cancellationToken.IsCancellationRequested) return;
        var review = Review!;
        _confirmedIntent = review.Preview.Command with { ExplicitlyConfirmed = true };
        _busy = true;
        Handoff = null;
        try
        {
            var result = await _coordinator.ConfirmAsync(review, cancellationToken);
            // A definite pre-journal rejection can safely return to review. No
            // uncertain or existing checkpoint is ever unlocked this way.
            if (result.Status == Sr5AfterRunRewardResolutionStatus.Blocked && result.Checkpoint is null)
            {
                _confirmedIntent = null;
                Review = null;
                Status = Sr5AfterRunRewardPhoneStatus.ReviewUnavailable;
                return;
            }
            await AcceptAsync(result, cancellationToken);
        }
        catch (Exception) { Status = Sr5AfterRunRewardPhoneStatus.OutcomeUnknown; }
        finally { _busy = false; }
    }

    public Task RecoverAsync(CancellationToken cancellationToken = default)
        => ResolveAsync(retry: false, cancellationToken);

    /// <summary>Explicit user retry only; coordinator looks up and uses original persisted command.</summary>
    public Task RetryAsync(CancellationToken cancellationToken = default)
        => ResolveAsync(retry: true, cancellationToken);

    private async Task ResolveAsync(bool retry, CancellationToken cancellationToken)
    {
        if (!CanRecover || retry && !CanRetry) return;
        _busy = true;
        Handoff = null;
        try
        {
            if (!OwnsSelection())
            {
                // A failed presenter reload may leave Error set even though the
                // committed file is intact. First clear that view error through
                // a read-only reload; no retry/Commit is allowed in this state.
                Status = Sr5AfterRunRewardPhoneStatus.RefreshRequired;
                try { await _refresh.ReloadAsync(_selection.WorkspaceId, cancellationToken); }
                catch (Exception) { return; }
                if (!OwnsSelection()) { ChangedRunner(); return; }
            }
            var result = retry
                ? await _coordinator.RetryAsync(OperationId, cancellationToken)
                : await _coordinator.RecoverAsync(OperationId, cancellationToken);
            await AcceptAsync(result, cancellationToken);
        }
        catch (Exception) { Status = Sr5AfterRunRewardPhoneStatus.OutcomeUnknown; }
        finally { _busy = false; }
    }

    private async Task AcceptAsync(Sr5AfterRunRewardResolution result, CancellationToken cancellationToken)
    {
        if (result.Checkpoint is { } checkpoint)
        {
            if (_confirmedIntent is null || checkpoint.Command != _confirmedIntent
                || checkpoint.OwnerId != _selection.OwnerId || !checkpoint.IsExact())
            {
                Status = Sr5AfterRunRewardPhoneStatus.OutcomeUnknown;
                return;
            }
            Retain(checkpoint);
        }
        if (!OwnsSelection()) { ChangedRunner(); return; }
        if (result.Status != Sr5AfterRunRewardResolutionStatus.Recorded
            || Checkpoint?.Phase != Sr5AfterRunRewardCheckpointPhase.Applied)
        {
            Status = Sr5AfterRunRewardPhoneStatus.OutcomeUnknown;
            return;
        }

        Status = Sr5AfterRunRewardPhoneStatus.RefreshRequired;
        try
        {
            await _refresh.ReloadAsync(_selection.WorkspaceId, cancellationToken);
            if (!OwnsSelection()) { ChangedRunner(); return; }
            var binding = _authority.Current;
            var fresh = await _coordinator.ReadAsync(cancellationToken);
            if (!OwnsSelection()) { ChangedRunner(); return; }
            if (binding != _authority.Current || fresh.Outcome != CharacterAfterRunRewardOutcome.Available
                || fresh.Snapshot is null) return;
            Handoff = new(binding, Checkpoint, fresh.Snapshot);
            Status = Sr5AfterRunRewardPhoneStatus.Recorded;
        }
        catch (Exception)
        {
            // The reward remains durable even if refreshing or leaving the page
            // is interrupted. Recovery re-looks up it; it never re-credits it.
            Status = Sr5AfterRunRewardPhoneStatus.RefreshRequired;
        }
    }

    private void Retain(Sr5AfterRunRewardCheckpoint checkpoint)
    {
        Checkpoint = checkpoint;
        _confirmedIntent = checkpoint.Command;
        OperationId = checkpoint.Command.OperationId;
        RewardId = checkpoint.Command.RewardId;
        Draft = new(checkpoint.Command.KarmaAmount.ToString(CultureInfo.InvariantCulture),
            checkpoint.Command.NuyenAmount.ToString(CultureInfo.InvariantCulture), checkpoint.Command.Reason,
            checkpoint.Command.ExpenseDateLocal.Date, checkpoint.Command.ExpenseDateLocal.TimeOfDay,
            checkpoint.Command.Kind == CharacterAfterRunRewardKind.NoAward);
    }

    private bool TryRequest(CultureInfo culture, out CharacterAfterRunRewardPreviewRequest? request)
    {
        request = null;
        if (!int.TryParse(Draft.Karma, NumberStyles.None, culture, out int karma)
            || !int.TryParse(Draft.Nuyen, NumberStyles.None, culture, out int nuyen)
            || karma < 0 || nuyen < 0 || Draft.Time < TimeSpan.Zero || Draft.Time >= TimeSpan.FromDays(1))
            return false;
        // DatePicker + TimePicker explicitly represent a local wall clock. Do
        // this conversion BEFORE Core Preview, never on a returned command.
        DateTime date = DateTime.SpecifyKind(Draft.Date.Date, DateTimeKind.Unspecified)
            .AddSeconds(Math.Truncate(Draft.Time.TotalSeconds));
        request = new(_selection.WorkspaceId, OperationId, RewardId, karma, nuyen, date, Draft.Reason,
            Kind: Draft.NoAward ? CharacterAfterRunRewardKind.NoAward : CharacterAfterRunRewardKind.Award);
        return CharacterAfterRunRewardProjector.IsValidPreviewRequest(request);
    }

    private bool OwnsSelection()
    {
        var current = _authority.Current;
        return current.IsCleanSavedSr5() && current.SameSelection(_selection);
    }

    private bool OwnsReloadableSelection()
    {
        var current = _authority.Current;
        return current.CanReloadSavedSr5() && current.SameSelection(_selection);
    }

    private void ChangedRunner()
    {
        Review = null;
        Handoff = null;
        RecordedRewards = [];
        Status = Sr5AfterRunRewardPhoneStatus.RunnerChanged;
    }

    private void EntryUnavailable()
    {
        Review = null;
        Handoff = null;
        RecordedRewards = [];
        Status = Sr5AfterRunRewardPhoneStatus.JournalUnavailable;
    }
}
