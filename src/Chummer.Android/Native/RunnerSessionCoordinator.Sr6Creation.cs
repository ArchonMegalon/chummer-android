using System.Runtime.CompilerServices;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Presentation;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

internal sealed record Sr6FoundationPhoneCommit(string Outcome, Sr6CreationFoundationCommit? Commit,
    Sr6CreationFoundationState? State, IReadOnlyList<string> Blockers);

public sealed partial class RunnerSessionCoordinator
{
    internal const string Sr6SaveNeedsRefresh = "sr6-foundation-save-needs-refresh";
    internal const string Sr6OutcomeUnknown = "sr6-foundation-save-outcome-unknown";
    private readonly ISr6CreationFoundationService? _sr6FoundationService;
    private readonly ConditionalWeakTable<Sr6CreationFoundationState, CharacterOverviewState> _sr6States = new();
    private readonly ConditionalWeakTable<Sr6CreationFoundationPreview, Sr6Review> _sr6Reviews = new();
    private readonly ConditionalWeakTable<Sr6CreationFoundationCommit, CharacterOverviewState> _sr6Commits = new();
    private Sr6CreationFoundationState? _sr6CurrentState;
    private Sr6CreationFoundationPreview? _sr6CurrentPreview;

    private sealed record Sr6Review(Sr6CreationFoundationState State, CharacterOverviewState Original,
        Sr6CreationFoundationConfirmRequest Command)
    {
        public bool Started { get; set; }
    }

    internal bool CanOpenSr6Foundation() => _sr6FoundationService is not null && IsSr6FoundationDisplay(State);

    private bool IsSr6FoundationDisplay(CharacterOverviewState original)
        => original.Profile is { Created: false, BuildMethod: Sr6CharacterCreationBuildMethods.Priority or Sr6CharacterCreationBuildMethods.SumToTen or Sr6CharacterCreationBuildMethods.PointBuy }
           && string.Equals(original.Rules?.GameEdition, "SR6", StringComparison.OrdinalIgnoreCase)
           && IsNativeEditDisplayCurrent(original);

    private static CharacterCreationFoundationResult<T> Sr6Stale<T>() where T : class
        => new(CharacterCreationFoundationOutcomes.Conflict, null, [Sr6CreationFoundationBlockers.StaleBinding]);

    internal bool IsSr6FoundationStateCurrent(Sr6CreationFoundationState state)
        => ReferenceEquals(_sr6CurrentState, state) && _sr6States.TryGetValue(state, out var original)
           && IsSr6FoundationDisplay(original) && Sr6MatchesDisplay(state, State);

    private static bool Sr6MatchesDisplay(Sr6CreationFoundationState state, CharacterOverviewState original)
        => state.Binding.WorkspaceId == original.WorkspaceId && state.Binding.ContentRevision == original.ContentRevision
           && state.Binding.SavedRevision == original.SavedRevision && state.BuildMethod == original.Profile?.BuildMethod;

    internal Task<CharacterCreationFoundationResult<Sr6CreationFoundationState>> LoadSr6FoundationAsync(
        CancellationToken ct = default, Func<bool>? isCurrentPage = null)
    {
        var original = State;
        return WithWorkspaceActivationGateAsync(async () =>
        {
            if (isCurrentPage?.Invoke() == false || !IsSr6FoundationDisplay(original)
                || _sr6FoundationService is not { } service || original.WorkspaceId is not { } id
                || original.DisplayOwnerContext is not { IsValid: true } owner) return Sr6Stale<Sr6CreationFoundationState>();
            var result = await Task.Run(() => service.Load(owner, id), ct);
            ct.ThrowIfCancellationRequested();
            if (isCurrentPage?.Invoke() == false || !IsSr6FoundationDisplay(original)) return Sr6Stale<Sr6CreationFoundationState>();
            return AcceptSr6Foundation(result, original);
        }, ct);
    }

    private CharacterCreationFoundationResult<Sr6CreationFoundationState> AcceptSr6Foundation(
        CharacterCreationFoundationResult<Sr6CreationFoundationState> result, CharacterOverviewState original)
    {
        _sr6CurrentState = null;
        _sr6CurrentPreview = null;
        if (result is not { Outcome: CharacterCreationFoundationOutcomes.Success, Value: { } state }) return result;
        if (!Sr6CreationFoundationIntegrity.ValidBinding(state.Binding) || !Sr6MatchesDisplay(state, original)
            || !IsSr6FoundationDisplay(original)) return Sr6Stale<Sr6CreationFoundationState>();
        var issued = _sr6States.GetValue(state, _ => original);
        if (!IsSr6FoundationDisplay(issued)) return Sr6Stale<Sr6CreationFoundationState>();
        _sr6CurrentState = state;
        return result;
    }

    internal Task<CharacterCreationFoundationResult<Sr6CreationFoundationPreview>> PreviewSr6FoundationAsync(
        Sr6CreationFoundationState state, Sr6CreationFoundationSelection selection, CancellationToken ct = default,
        Func<bool>? isCurrentPage = null)
    {
        if (!Sr6CreationFoundationIntegrity.TryFreezeSelection(selection, out var frozen))
            return Task.FromResult(new CharacterCreationFoundationResult<Sr6CreationFoundationPreview>(
                CharacterCreationFoundationOutcomes.Invalid, null, [Sr6CreationPriorityBlockers.CategoriesInvalid]));
        return WithWorkspaceActivationGateAsync(async () =>
        {
            if (isCurrentPage?.Invoke() == false || !IsSr6FoundationStateCurrent(state)
                || !_sr6States.TryGetValue(state, out var original) || original.DisplayOwnerContext is not { IsValid: true } owner
                || _sr6FoundationService is not { } service) return Sr6Stale<Sr6CreationFoundationPreview>();
            _sr6CurrentPreview = null;
            var result = await Task.Run(() => service.Preview(owner, state.Binding, frozen), ct);
            ct.ThrowIfCancellationRequested();
            if (isCurrentPage?.Invoke() == false || !IsSr6FoundationStateCurrent(state)) return Sr6Stale<Sr6CreationFoundationPreview>();
            if (result is { Outcome: CharacterCreationFoundationOutcomes.Success, Value: { } preview })
            {
                if (preview.Binding != state.Binding || Sr6CreationFoundationIntegrity.Digest(preview.Selection) != Sr6CreationFoundationIntegrity.Digest(frozen)
                    || preview.PreviewDigest != Sr6CreationFoundationIntegrity.PreviewDigest(preview)) return Sr6Stale<Sr6CreationFoundationPreview>();
                _sr6Reviews.Add(preview, new(state, original, new(state.Binding, frozen, preview.PreviewDigest, Guid.NewGuid(), true)));
                _sr6CurrentPreview = preview;
            }
            return result;
        }, ct);
    }

    internal bool IsSr6FoundationPreviewCurrent(Sr6CreationFoundationPreview preview)
        => ReferenceEquals(_sr6CurrentPreview, preview) && _sr6Reviews.TryGetValue(preview, out var issued)
           && !issued.Started && IsSr6FoundationStateCurrent(issued.State);

    internal bool CanDisplaySr6FoundationCommit(Sr6CreationFoundationCommit commit)
        => _sr6Commits.TryGetValue(commit, out var original) && IsPrerequisiteOriginalOwnerVisible(original);

    internal Task<Sr6FoundationPhoneCommit> ConfirmSr6FoundationAsync(Sr6CreationFoundationPreview preview,
        bool explicitlyConfirmed, CancellationToken ct = default, Func<bool>? isCurrentPage = null)
    {
        if (!explicitlyConfirmed || !_sr6Reviews.TryGetValue(preview, out var issued))
            return Task.FromResult(Rejected(Sr6CreationFoundationBlockers.ConfirmationRequired));
        return WithWorkspaceActivationGateAsync(async () =>
        {
            if (isCurrentPage?.Invoke() == false || !IsSr6FoundationPreviewCurrent(preview)
                || issued.Original.DisplayOwnerContext is not { IsValid: true } owner
                || _sr6FoundationService is not { } service) return Rejected(Sr6CreationFoundationBlockers.StaleBinding);
            ct.ThrowIfCancellationRequested();
            issued.Started = true;
            int entered = 0;
            CharacterCreationFoundationResult<Sr6CreationFoundationCommit> result;
            try
            {
                result = await Task.Run(() => { Interlocked.Exchange(ref entered, 1); return service.Confirm(owner, issued.Command); }, ct);
            }
            catch (OperationCanceledException) when (Volatile.Read(ref entered) == 0) { issued.Started = false; throw; }
            catch (Exception error) when (error is not OutOfMemoryException) { return Rejected(Sr6OutcomeUnknown); }
            if (result is not { Outcome: CharacterCreationFoundationOutcomes.Success, Value: { } commit })
                return new(result.Outcome, null, null, result.Blockers);
            if (Sr6CreationFoundationIntegrity.Digest(commit.Decision.Command) != Sr6CreationFoundationIntegrity.Digest(issued.Command)
                || commit.Decision.CommittedContentRevision != issued.Command.Binding.ContentRevision + 1
                || commit.Decision.DecisionDigest != Sr6CreationFoundationIntegrity.DecisionDigest(commit.Decision))
                return Rejected(Sr6OutcomeUnknown);
            _sr6Commits.Add(commit, issued.Original);
            _sr6CurrentState = null;
            _sr6CurrentPreview = null;
            try
            {
                // After a durable save, cancellation/departure affects only refresh.
                if (ct.IsCancellationRequested || isCurrentPage?.Invoke() == false || !CanDisplaySr6FoundationCommit(commit)
                    || _presenter is not IOwnerBoundWorkspaceRefreshPresenter refresh) return SavedNeedsRefresh();
                await refresh.LoadAsync(owner, issued.Command.Binding.WorkspaceId, ct);
                if (ct.IsCancellationRequested || isCurrentPage?.Invoke() == false || !CanDisplaySr6FoundationCommit(commit)) return SavedNeedsRefresh();
                await SyncShellAsync(ct);
                var original = State;
                var loaded = await Task.Run(() => service.Load(owner, issued.Command.Binding.WorkspaceId), ct);
                if (ct.IsCancellationRequested || isCurrentPage?.Invoke() == false || !CanDisplaySr6FoundationCommit(commit)) return SavedNeedsRefresh();
                var accepted = AcceptSr6Foundation(loaded, original);
                if (accepted.Value is not { } current || current.Binding.ContentRevision != commit.Decision.CommittedContentRevision
                    || current.Selection?.PreviewDigest != commit.Decision.Preview.PreviewDigest) return SavedNeedsRefresh();
                return new(result.Outcome, commit, current, []);
            }
            catch (Exception error) when (error is not OutOfMemoryException) { return SavedNeedsRefresh(); }
            Sr6FoundationPhoneCommit SavedNeedsRefresh() => new(result.Outcome, commit, null, [Sr6SaveNeedsRefresh]);
        }, ct);

        static Sr6FoundationPhoneCommit Rejected(string blocker)
            => new(CharacterCreationFoundationOutcomes.Conflict, null, null, [blocker]);
    }
}
