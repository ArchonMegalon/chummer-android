using System.Runtime.CompilerServices;
using Chummer.Contracts.Characters;
using Chummer.Presentation;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed partial class RunnerSessionCoordinator
{
    private sealed record SkillsLoad(CharacterOverviewState Display, CharacterCreationSkillsState State);
    private sealed record SkillsPreview(SkillsLoad Load, CharacterCreationSkillAllocation[] Allocations, CharacterCreationSkillGroupAllocation[] Groups)
    {
        public bool ConfirmationStarted { get; set; }
    }

    private readonly ConditionalWeakTable<CharacterCreationSkillsBinding, SkillsLoad> _skillLoads = new();
    private readonly ConditionalWeakTable<CharacterCreationSkillsPreview, SkillsPreview> _skillPreviews = new();
    private readonly ConditionalWeakTable<CharacterCreationSkillsReceipt, CharacterOverviewState> _skillReceipts = new();

    private static CharacterCreationFoundationResult<T> SkillsUnavailable<T>() where T : class
        => new(CharacterCreationFoundationOutcomes.Blocked, null, [CharacterCreationSkillsBlockers.WorkspaceUnavailable]);

    private bool SkillsDisplayCurrent(CharacterOverviewState original)
        => original.Profile?.Created == false && original.DisplayOwnerContext is { IsValid: true }
           && original.Session.OwnerContext == original.DisplayOwnerContext && IsNativeEditDisplayCurrent(original);

    internal bool IsCreationSkillsStateCurrent(CharacterCreationSkillsState state)
        => _skillLoads.TryGetValue(state.Binding, out var issued)
           && ReferenceEquals(issued.State, state) && SkillsDisplayCurrent(issued.Display)
           && CreationSkillsPhoneAuthority.MatchesOverview(state, State);

    public CharacterCreationFoundationResult<CharacterCreationSkillsState> LoadCreationSkills()
        => LoadCreationSkillsForDisplay(State);

    private CharacterCreationFoundationResult<CharacterCreationSkillsState> LoadCreationSkillsForDisplay(
        CharacterOverviewState original)
    {
        if (!SkillsDisplayCurrent(original) || original.WorkspaceId is not { } id
            || original.DisplayOwnerContext is not { } owner || _ownerBoundSkillsService is not { } service)
            return SkillsUnavailable<CharacterCreationSkillsState>();
        var result = service.Load(owner, new(id));
        if (!SkillsDisplayCurrent(original)
            || result.Value is { } candidate && !CreationSkillsPhoneAuthority.MatchesOverview(candidate, State))
            return SkillsUnavailable<CharacterCreationSkillsState>();
        if (result is { Outcome: CharacterCreationFoundationOutcomes.Success, Value: { } state })
        {
            var issued = _skillLoads.GetValue(state.Binding, _ => new(original, state));
            if (!ReferenceEquals(issued.State, state) || !SkillsDisplayCurrent(issued.Display))
                return SkillsUnavailable<CharacterCreationSkillsState>();
        }
        return result;
    }

    internal CharacterCreationFoundationResult<CharacterCreationSkillsState> RevalidateCreationSkills(
        CharacterCreationSkillsState issued)
        => _skillLoads.TryGetValue(issued.Binding, out var load) && ReferenceEquals(load.State, issued)
            ? LoadCreationSkillsForDisplay(load.Display) : SkillsUnavailable<CharacterCreationSkillsState>();

    internal CharacterCreationFoundationResult<CharacterCreationSkillsPreview> PreviewCreationSkills(
        CharacterCreationSkillsBinding binding, IReadOnlyList<CharacterCreationSkillAllocation> allocations,
        IReadOnlyList<CharacterCreationSkillGroupAllocation> groups)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(allocations);
        ArgumentNullException.ThrowIfNull(groups);
        if (!_skillLoads.TryGetValue(binding, out var issued) || !IsCreationSkillsStateCurrent(issued.State)
            || issued.Display.DisplayOwnerContext is not { } owner || _ownerBoundSkillsService is not { } service)
            return SkillsUnavailable<CharacterCreationSkillsPreview>();
        var live = LoadCreationSkillsForDisplay(issued.Display);
        if (live.Value is not { } state || !CreationSkillsPhoneAuthority.IsReady(state, State)
            || !CreationSkillsPhoneAuthority.BindingEquals(binding, state.Binding))
            return SkillsUnavailable<CharacterCreationSkillsPreview>();
        var copied = SortSkills(allocations);
        var copiedGroups = SortGroups(groups);
        var result = service.Preview(owner, new(binding, copied, copiedGroups));
        if (!IsCreationSkillsStateCurrent(issued.State)) return SkillsUnavailable<CharacterCreationSkillsPreview>();
        if (result is { Outcome: CharacterCreationFoundationOutcomes.Success, Value: { } preview })
            _skillPreviews.GetValue(preview, _ => new(issued, copied, copiedGroups));
        return result;
    }

    internal bool IsCreationSkillsPreviewCurrent(CharacterCreationSkillsPreview preview)
        => _skillPreviews.TryGetValue(preview, out var issued) && !issued.ConfirmationStarted
           && IsCreationSkillsStateCurrent(issued.Load.State);

    internal bool CanDisplayCreationSkillsPreview(CharacterCreationSkillsPreview preview)
        => _skillPreviews.TryGetValue(preview, out var issued)
           && IsNativePersistenceOwnerCurrent(issued.Load.Display.DisplayOwnerContext)
           && State.WorkspaceId == issued.Load.Display.WorkspaceId
           && State.DisplayOwnerContext == issued.Load.Display.DisplayOwnerContext
           && State.Session.OwnerContext == issued.Load.Display.DisplayOwnerContext;

    internal bool IsCreationSkillsReceiptCurrent(CharacterCreationSkillsReceipt receipt)
        => _skillReceipts.TryGetValue(receipt, out var original)
           && IsNativePersistenceOwnerCurrent(original.DisplayOwnerContext)
           && State.DisplayOwnerContext == original.DisplayOwnerContext && State.Session.OwnerContext == original.DisplayOwnerContext
           && State.Error is null && State.WorkspaceId == receipt.WorkspaceId
           && State.ContentRevision == receipt.ContentRevision && State.SavedRevision == receipt.SavedRevision;

    internal Task<CreationSkillsPhoneConfirmResult> ConfirmCreationSkillsAsync(
        CharacterCreationSkillsPreview preview, IReadOnlyList<CharacterCreationSkillAllocation> allocations,
        IReadOnlyList<CharacterCreationSkillGroupAllocation> groups, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(allocations);
        ArgumentNullException.ThrowIfNull(groups);
        var copied = SortSkills(allocations);
        var copiedGroups = SortGroups(groups);
        return WithWorkspaceActivationGateAsync(() => ConfirmIssuedSkillsAsync(preview, copied, copiedGroups, idempotencyKey, cancellationToken), cancellationToken);
    }

    private async Task<CreationSkillsPhoneConfirmResult> ConfirmIssuedSkillsAsync(
        CharacterCreationSkillsPreview preview, CharacterCreationSkillAllocation[] allocations,
        CharacterCreationSkillGroupAllocation[] groups, string idempotencyKey, CancellationToken ct)
    {
        if (!_skillPreviews.TryGetValue(preview, out var issued) || !IsCreationSkillsPreviewCurrent(preview)
            || !allocations.SequenceEqual(issued.Allocations) || !groups.SequenceEqual(issued.Groups)
            || idempotencyKey != CreationSkillsPhoneAuthority.ComputeIdempotencyKey(preview, allocations, groups)
            || _ownerBoundSkillsService is not { } service
            || issued.Load.Display.DisplayOwnerContext is not { } owner)
            return RejectedSkills(CharacterCreationSkillsBlockers.StaleWorkspaceRevision);
        var original = issued.Load.Display;
        var live = await Task.Run(() => service.Load(owner, new(preview.Binding.WorkspaceId)), ct);
        if (!IsCreationSkillsPreviewCurrent(preview) || live.Value is not { } state
            || !CreationSkillsPhoneAuthority.CanConfirmPreview(state, original, preview, allocations, groups))
            return RejectedSkills(CharacterCreationSkillsBlockers.PreviewDigestMismatch);
        var reprojection = await Task.Run(() => service.Preview(owner, new(preview.Binding, allocations, groups)), ct);
        if (!IsCreationSkillsPreviewCurrent(preview)
            || !CreationSkillsPhoneAuthority.CanAdoptPreview(state, original, reprojection, allocations, groups)
            || reprojection.Value is not { } canonical || !CreationSkillsPhoneAuthority.CanonicallyEquals(preview, canonical))
            return RejectedSkills(CharacterCreationSkillsBlockers.PreviewDigestMismatch);

        ct.ThrowIfCancellationRequested();
        issued.ConfirmationStarted = true;
        int entered = 0;
        CharacterCreationFoundationResult<CharacterCreationSkillsReceipt> result;
        try
        {
            // Core acquires and releases the exact original owner's lease on this
            // worker, including the atomic checkpoint; no lease crosses an await.
            result = await Task.Run(() =>
            {
                Interlocked.Exchange(ref entered, 1);
                return service.Confirm(owner, new(canonical.Binding, allocations, groups, canonical.PreviewDigest, idempotencyKey, true));
            }, ct);
        }
        catch (OperationCanceledException) when (Volatile.Read(ref entered) == 0)
        { issued.ConfirmationStarted = false; throw; }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // Do not automatically replay an
            // uncertain write or present it as a known failure without a commit.
            return new("outcome-unknown", null, null, ["creation-skills-confirm-outcome-unknown"]);
        }
        if (result is not { Outcome: CharacterCreationFoundationOutcomes.Success, Value: { } receipt })
            return new(result.Outcome, result.Value, null, result.Blockers);
        _skillReceipts.GetValue(receipt, _ => original);
        try
        {
            var committed = await Task.Run(() => service.Load(owner, new(receipt.WorkspaceId)));
            if (committed.Value is not { } saved
                || !CreationSkillsPhoneAuthority.ReceiptMatchesBeforeActivation(receipt, canonical, saved, original, idempotencyKey)
                || !IsNativePersistenceOwnerCurrent(owner)
                || _presenter is not IOwnerBoundWorkspaceRefreshPresenter bound)
                return NeedsReopen();
            await bound.LoadAsync(owner, receipt.WorkspaceId, ct);
            if (!IsCreationSkillsReceiptCurrent(receipt)) return NeedsReopen();
            await SyncShellAsync(ct);
            if (!IsCreationSkillsReceiptCurrent(receipt)) return NeedsReopen();
            var refreshed = LoadCreationSkills();
            if (refreshed.Value is not { } fresh || !IsCreationSkillsStateCurrent(fresh)
                || !CreationSkillsPhoneAuthority.ReceiptMatches(receipt, canonical, fresh, State, idempotencyKey))
                return NeedsReopen();
            _notice = "Skills draft saved. Character effects remain pending finalization.";
            NotifyChanged();
            return IsCreationSkillsReceiptCurrent(receipt)
                ? new(CharacterCreationFoundationOutcomes.Success, receipt, fresh, []) : NeedsReopen();
        }
        catch (Exception error) when (error is not OutOfMemoryException) { return NeedsReopen(); }
        CreationSkillsPhoneConfirmResult NeedsReopen() => new(CharacterCreationFoundationOutcomes.Success,
            receipt, null, [CharacterCreationSkillsBlockers.PostCommitRefreshRequired]);
    }

    private static CharacterCreationSkillAllocation[] SortSkills(IEnumerable<CharacterCreationSkillAllocation> items)
        => items.OrderBy(x => x.Kind, StringComparer.Ordinal).ThenBy(x => x.SourceSkillId, StringComparer.Ordinal).ToArray();
    private static CharacterCreationSkillGroupAllocation[] SortGroups(IEnumerable<CharacterCreationSkillGroupAllocation> items)
        => items.OrderBy(x => x.GroupId, StringComparer.Ordinal).ToArray();

    private static CreationSkillsPhoneConfirmResult RejectedSkills(string blocker)
        => new(CharacterCreationFoundationOutcomes.Conflict, null, null, [blocker]);
}
