using System.Globalization;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;

namespace Chummer.Android.Native;

public enum Sr5CareerReputationPhoneStatus
{ Loading, Editing, Review, InvalidInput, ReviewUnavailable, Unavailable, Pending, OutcomeUnknown, Recorded, Superseded, RefreshRequired, RunnerChanged }

/// <summary>Empty means unchanged, not zero. Signed deltas are user intent, never calculated totals.</summary>
public sealed record Sr5CareerReputationPhoneDraft(string StreetCred, string Notoriety, string PublicAwareness, string Reason);

/// <summary>
/// One native page's local intent, Core review and recovery state. No rules or
/// workspace XML writes live here. A canceled page cannot discard a submitted
/// identity, and a failed presenter reload cannot turn a saved result into failure.
/// </summary>
public sealed class Sr5CareerReputationPhoneModel
{
    private readonly Sr5CareerReputationCoordinator _coordinator;
    private readonly ISr5AfterRunRewardRunnerAuthority _authority;
    private readonly ISr5AfterRunRewardSavedRunnerRefresh _refresh;
    private readonly Sr5AfterRunRewardRunnerBinding _selection;
    private CharacterCareerReputationCommand? _intent;
    private Sr5AfterRunRewardRunnerBinding? _freshBinding;
    private bool _initialized, _busy;

    public Sr5CareerReputationPhoneModel(Sr5CareerReputationCoordinator coordinator,
        ISr5AfterRunRewardRunnerAuthority authority, ISr5AfterRunRewardSavedRunnerRefresh refresh)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _authority = authority ?? throw new ArgumentNullException(nameof(authority));
        _refresh = refresh ?? throw new ArgumentNullException(nameof(refresh));
        _selection = authority.Current;
    }

    public Guid OperationId { get; private set; } = Guid.NewGuid();
    public Sr5CareerReputationPhoneDraft Draft { get; private set; } = new("", "", "", "");
    public Sr5CareerReputationPhoneStatus Status { get; private set; } = Sr5CareerReputationPhoneStatus.Loading;
    public Sr5CareerReputationReview? Review { get; private set; }
    public CharacterCareerReputationSnapshot? Snapshot { get; private set; }
    public Sr5CareerReputationCheckpoint? Checkpoint { get; private set; }
    public IReadOnlyList<Sr5CareerReputationCheckpoint> History { get; private set; } = [];
    public IReadOnlyList<Sr5CareerReputationCheckpoint> SupersededHistory { get; private set; } = [];
    public bool IsBusy => _busy;
    public bool HasRetainedIntent => _intent is not null;
    public bool HasCurrentSelection => OwnsReloadableSelection();
    public bool CanEdit => _initialized && !_busy && _intent is null && OwnsSelection()
        && Status != Sr5CareerReputationPhoneStatus.Unavailable;
    public bool CanConfirm => CanEdit && Status == Sr5CareerReputationPhoneStatus.Review && Review?.Runner == _authority.Current;
    public bool CanRecover => !_busy && _intent is not null && OwnsReloadableSelection();
    public bool CanRetry => CanRecover && OwnsSelection() && Checkpoint?.IsTerminal != true;
    public bool CanFinish => !_busy && _freshBinding is not null && _freshBinding == _authority.Current
        && OwnsSelection() && Status is Sr5CareerReputationPhoneStatus.Recorded or Sr5CareerReputationPhoneStatus.Superseded;

    public bool UpdateDraft(Sr5CareerReputationPhoneDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (!CanEdit) return false;
        if (Draft != draft) { Draft = draft; Review = null; Status = Sr5CareerReputationPhoneStatus.Editing; }
        return true;
    }

    /// <summary>Appearance observes pending intent and facts only; it never retries a mutation.</summary>
    public async Task InitializeAsync(CancellationToken token = default)
    {
        if (_busy) return;
        if (!OwnsSelection()) { ChangedRunner(); return; }
        if (_initialized && HasRetainedIntent) return;
        _busy = true;
        try
        {
            var state = await _coordinator.InspectAsync(token);
            if (!OwnsSelection()) { ChangedRunner(); return; }
            _initialized = true;
            History = state.History;
            SupersededHistory = state.SupersededHistory;
            if (state.RecoveryRequired is { } pending) { Retain(pending); Status = Sr5CareerReputationPhoneStatus.Pending; return; }
            var read = await _coordinator.ReadAsync(token);
            if (!OwnsSelection()) { ChangedRunner(); return; }
            Snapshot = read.Snapshot;
            Status = read.Outcome == CharacterCareerReputationOutcome.Available && Snapshot is not null
                ? (Review?.Runner == _authority.Current ? Sr5CareerReputationPhoneStatus.Review : Sr5CareerReputationPhoneStatus.Editing)
                : Sr5CareerReputationPhoneStatus.Unavailable;
            if (Status != Sr5CareerReputationPhoneStatus.Review) Review = null;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception) { Review = null; Status = Sr5CareerReputationPhoneStatus.Unavailable; }
        finally { _busy = false; }
    }

    public async Task PreviewAsync(bool burnStreetCred = false, CultureInfo? culture = null, CancellationToken token = default)
    {
        if (!CanEdit || token.IsCancellationRequested) return;
        Review = null;
        if (!TryRequest(burnStreetCred, culture ?? CultureInfo.CurrentCulture, out var request))
        { Status = Sr5CareerReputationPhoneStatus.InvalidInput; return; }
        _busy = true;
        try
        {
            var result = await _coordinator.PreviewAsync(request!, token);
            if (!OwnsSelection()) { ChangedRunner(); return; }
            Review = result.Review;
            if (Review is not null) Snapshot = Review.Snapshot;
            Status = Review is not null ? Sr5CareerReputationPhoneStatus.Review : Sr5CareerReputationPhoneStatus.ReviewUnavailable;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { Status = Sr5CareerReputationPhoneStatus.Editing; }
        catch (Exception) { Status = Sr5CareerReputationPhoneStatus.ReviewUnavailable; }
        finally { _busy = false; }
    }

    public async Task ConfirmAsync(CancellationToken token = default)
    {
        if (!CanConfirm || token.IsCancellationRequested) return;
        var review = Review!;
        _intent = review.Preview.Command with { ExplicitlyConfirmed = true };
        _busy = true;
        _freshBinding = null;
        try
        {
            var result = await _coordinator.ConfirmAsync(review, token);
            if (result.Status == Sr5CareerReputationResolutionStatus.Blocked && result.Checkpoint is null)
            { _intent = null; Review = null; Status = Sr5CareerReputationPhoneStatus.ReviewUnavailable; return; }
            await AcceptAsync(result, token);
        }
        catch (Exception) { Status = Sr5CareerReputationPhoneStatus.OutcomeUnknown; }
        finally { _busy = false; }
    }

    public async Task ResumeHistoryAsync(Guid operation, CancellationToken token = default)
    {
        if (!CanEdit || token.IsCancellationRequested) return;
        _busy = true;
        Review = null;
        try
        {
            var state = await _coordinator.InspectAsync(token);
            if (!OwnsSelection()) { ChangedRunner(); return; }
            History = state.History;
            SupersededHistory = state.SupersededHistory;
            if (state.RecoveryRequired is { } pending)
            { Retain(pending); Status = Sr5CareerReputationPhoneStatus.Pending; return; }
            var selected = state.History.Concat(state.SupersededHistory).SingleOrDefault(entry => entry.Command.Request.OperationId == operation);
            if (selected is null) { Status = Sr5CareerReputationPhoneStatus.Editing; return; }
            Retain(selected);
            await AcceptAsync(await _coordinator.RecoverAsync(OperationId, token), token);
        }
        catch (Exception) { Status = HasRetainedIntent ? Sr5CareerReputationPhoneStatus.OutcomeUnknown : Sr5CareerReputationPhoneStatus.Unavailable; }
        finally { _busy = false; }
    }

    public Task RecoverAsync(CancellationToken token = default) => ResolveAsync(0, token);
    public Task RetryAsync(CancellationToken token = default) => ResolveAsync(1, token);
    public Task CloseSupersededAsync(CancellationToken token = default) => ResolveAsync(2, token);

    private async Task ResolveAsync(int mode, CancellationToken token)
    {
        if (!CanRecover || mode != 0 && !CanRetry || token.IsCancellationRequested) return;
        _busy = true;
        _freshBinding = null;
        try
        {
            if (!OwnsSelection())
            {
                Status = Sr5CareerReputationPhoneStatus.RefreshRequired;
                await _refresh.ReloadAsync(_selection.WorkspaceId, token);
                if (!OwnsSelection()) { ChangedRunner(); return; }
            }
            var result = mode switch
            {
                1 => await _coordinator.RetryAsync(OperationId, token),
                2 => await _coordinator.CloseSupersededAsync(OperationId, token),
                _ => await _coordinator.RecoverAsync(OperationId, token)
            };
            await AcceptAsync(result, token);
        }
        catch (Exception) { Status = Sr5CareerReputationPhoneStatus.OutcomeUnknown; }
        finally { _busy = false; }
    }

    private async Task AcceptAsync(Sr5CareerReputationResolution result, CancellationToken token)
    {
        if (result.Checkpoint is { } checkpoint)
        {
            if (_intent is null || checkpoint.Command != _intent || checkpoint.OwnerId != _selection.OwnerId || !checkpoint.IsExact())
            { Status = Sr5CareerReputationPhoneStatus.OutcomeUnknown; return; }
            Retain(checkpoint);
        }
        if (!OwnsSelection()) { ChangedRunner(); return; }
        bool saved = result.Status == Sr5CareerReputationResolutionStatus.Recorded && Checkpoint?.Phase == Sr5CareerReputationPhase.Applied;
        bool closed = result.Status == Sr5CareerReputationResolutionStatus.Superseded && Checkpoint?.Phase == Sr5CareerReputationPhase.Superseded;
        if (!saved && !closed) { Status = Sr5CareerReputationPhoneStatus.OutcomeUnknown; return; }
        Status = Sr5CareerReputationPhoneStatus.RefreshRequired;
        try
        {
            await _refresh.ReloadAsync(_selection.WorkspaceId, token);
            if (!OwnsSelection()) { ChangedRunner(); return; }
            var before = _authority.Current;
            var read = await _coordinator.ReadAsync(token);
            if (!OwnsSelection()) { ChangedRunner(); return; }
            if (before != _authority.Current || read.Outcome != CharacterCareerReputationOutcome.Available || read.Snapshot is null) return;
            Snapshot = read.Snapshot;
            _freshBinding = before;
            Status = saved ? Sr5CareerReputationPhoneStatus.Recorded : Sr5CareerReputationPhoneStatus.Superseded;
        }
        catch (Exception) { Status = Sr5CareerReputationPhoneStatus.RefreshRequired; }
    }

    private void Retain(Sr5CareerReputationCheckpoint checkpoint)
    {
        Checkpoint = checkpoint;
        _intent = checkpoint.Command;
        OperationId = checkpoint.Command.Request.OperationId;
        var adjustment = checkpoint.Command.Request.Adjustment;
        string Value(int? value) => value?.ToString(CultureInfo.CurrentCulture) ?? "";
        Draft = new(Value(adjustment?.StreetCredDelta), Value(adjustment?.NotorietyDelta),
            Value(adjustment?.PublicAwarenessDelta), checkpoint.Command.Request.Reason);
    }

    private bool TryRequest(bool burn, CultureInfo culture, out CharacterCareerReputationRequest? request)
    {
        request = null;
        bool Parse(string value, out int? delta)
        {
            delta = null;
            if (string.IsNullOrWhiteSpace(value)) return true;
            if (value.Length > 12 || !int.TryParse(value, NumberStyles.Integer, culture, out int number)) return false;
            delta = number;
            return true;
        }
        CharacterCareerReputationAdjustment? adjustment = null;
        if (!burn)
        {
            if (!Parse(Draft.StreetCred, out int? streetCred) || !Parse(Draft.Notoriety, out int? notoriety)
                || !Parse(Draft.PublicAwareness, out int? publicAwareness)) return false;
            adjustment = new(streetCred, notoriety, publicAwareness);
        }
        request = new(_selection.WorkspaceId, OperationId, burn ? CharacterCareerReputationOperation.BurnStreetCred
            : CharacterCareerReputationOperation.AdjustManualAwards, adjustment, Draft.Reason);
        return CharacterCareerReputationTransaction.IsValidRequest(request);
    }

    private bool OwnsSelection()
    { var current = _authority.Current; return current.IsCleanSavedSr5() && current.SameSelection(_selection); }
    private bool OwnsReloadableSelection()
    { var current = _authority.Current; return current.CanReloadSavedSr5() && current.SameSelection(_selection); }
    private void ChangedRunner()
    { Review = null; Snapshot = null; _freshBinding = null; History = []; SupersededHistory = []; Status = Sr5CareerReputationPhoneStatus.RunnerChanged; }
}
