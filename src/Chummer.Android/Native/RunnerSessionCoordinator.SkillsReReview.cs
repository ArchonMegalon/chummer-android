using System.Runtime.CompilerServices;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Presentation;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed partial class RunnerSessionCoordinator
{
    private sealed record SkillsReviewLoad(CharacterOverviewState Display, CharacterCreationSkillsReReviewState State);
    private sealed record SkillsReviewPreview(SkillsReviewLoad Load,
        CharacterCreationSkillAllocation[] Skills, CharacterCreationSkillGroupAllocation[] Groups)
    {
        public bool ConfirmationStarted { get; set; }
    }
    private readonly ConditionalWeakTable<CharacterCreationSkillsReReviewBinding, SkillsReviewLoad> _skillsReviewLoads = new();
    private readonly ConditionalWeakTable<CharacterCreationSkillsReReviewPreview, SkillsReviewPreview> _skillsReviewPreviews = new();

    internal bool IsCreationSkillsReReviewStateCurrent(CharacterCreationSkillsReReviewState state)
        => _skillsReviewLoads.TryGetValue(state.Binding, out var issued) && ReferenceEquals(issued.State, state)
           && SkillsDisplayCurrent(issued.Display) && CreationSkillsReReviewPhoneAuthority.Matches(state, State);

    internal bool CanDisplayCreationSkillsReReviewReceipt(CharacterCreationSkillsReceipt receipt)
        => _skillReceipts.TryGetValue(receipt, out var original)
           && IsNativePersistenceOwnerCurrent(original.DisplayOwnerContext)
           && State.DisplayOwnerContext == original.DisplayOwnerContext
           && State.Session.OwnerContext == original.DisplayOwnerContext
           && State.WorkspaceId == receipt.WorkspaceId;

    internal CharacterCreationFoundationResult<CharacterCreationSkillsReReviewState> LoadCreationSkillsReReview()
    {
        var before = State;
        if (!SkillsDisplayCurrent(before) || before.WorkspaceId is not { } id
            || before.DisplayOwnerContext is not { } owner
            || _ownerBoundSkillsService is not IOwnerBoundCharacterCreationSkillsReReviewService service)
            return SkillsUnavailable<CharacterCreationSkillsReReviewState>();
        var result = service.LoadReReview(owner, new(id));
        if (!SkillsDisplayCurrent(before) || result.Value is { } candidate
            && (!CreationSkillsReReviewPhoneAuthority.Matches(candidate, before)
                || !CreationSkillsReReviewPhoneAuthority.Matches(candidate, State)))
            return SkillsUnavailable<CharacterCreationSkillsReReviewState>();
        if (result is { Outcome: CharacterCreationFoundationOutcomes.Success, Value: { } state })
        {
            var issued = _skillsReviewLoads.GetValue(state.Binding, _ => new(before, state));
            if (!ReferenceEquals(issued.State, state) || !SkillsDisplayCurrent(before))
                return SkillsUnavailable<CharacterCreationSkillsReReviewState>();
            _skillsReviewPreviews.GetValue(state.InitialPreview, _ => new(issued,
                SortSkills(state.HistoricalDraft.Allocations), SortGroups(state.HistoricalDraft.GroupAllocations)));
        }
        return result;
    }

    internal CharacterCreationFoundationResult<CharacterCreationSkillsReReviewPreview> PreviewCreationSkillsReReview(
        CharacterCreationSkillsReReviewBinding binding,
        IReadOnlyList<CharacterCreationSkillAllocation> skills, IReadOnlyList<CharacterCreationSkillGroupAllocation> groups)
    {
        if (!_skillsReviewLoads.TryGetValue(binding, out var issued)
            || !IsCreationSkillsReReviewStateCurrent(issued.State)
            || issued.Display.DisplayOwnerContext is not { } owner
            || _ownerBoundSkillsService is not IOwnerBoundCharacterCreationSkillsReReviewService service)
            return SkillsUnavailable<CharacterCreationSkillsReReviewPreview>();
        var copied = SortSkills(skills);
        var copiedGroups = SortGroups(groups);
        var result = service.PreviewReReview(owner, new(binding, copied, copiedGroups));
        if (!IsCreationSkillsReReviewStateCurrent(issued.State) || result.Value is { } candidate
            && !CreationSkillsReReviewPhoneAuthority.ValidPreview(issued.State, candidate, copied, copiedGroups))
            return SkillsUnavailable<CharacterCreationSkillsReReviewPreview>();
        if (result.Value is { } preview
            && result.Outcome is CharacterCreationFoundationOutcomes.Success or CharacterCreationFoundationOutcomes.Blocked)
            _skillsReviewPreviews.GetValue(preview, _ => new(issued, copied, copiedGroups));
        return result;
    }

    internal Task<CreationSkillsPhoneConfirmResult> ConfirmCreationSkillsReReviewAsync(
        CharacterCreationSkillsReReviewPreview preview,
        IReadOnlyList<CharacterCreationSkillAllocation> skills, IReadOnlyList<CharacterCreationSkillGroupAllocation> groups,
        bool explicitlyReviewed, CancellationToken cancellationToken = default)
    {
        var selectedSkills = SortSkills(skills);
        var selectedGroups = SortGroups(groups);
        return WithWorkspaceActivationGateAsync(async () =>
        {
            if (!explicitlyReviewed)
                return new CreationSkillsPhoneConfirmResult(CharacterCreationFoundationOutcomes.Invalid, null, null,
                    [CharacterCreationSkillsReReviewSchemas.ExplicitReviewRequired]);
            if (!_skillsReviewPreviews.TryGetValue(preview, out var issued) || issued.ConfirmationStarted
                || !IsCreationSkillsReReviewStateCurrent(issued.Load.State)
                || !selectedSkills.SequenceEqual(issued.Skills) || !selectedGroups.SequenceEqual(issued.Groups)
                || issued.Load.Display.DisplayOwnerContext is not { } owner
                || _ownerBoundSkillsService is not IOwnerBoundCharacterCreationSkillsReReviewService service
                || !CreationSkillsReReviewPhoneAuthority.ValidPreview(issued.Load.State, preview, selectedSkills, selectedGroups)
                || !preview.CurrentPreview.CanConfirm || preview.CurrentPreview.Blockers.Count != 0)
                return RejectedSkills(CharacterCreationSkillsReReviewSchemas.Stale);
            var canonical = await Task.Run(() => service.PreviewReReview(owner,
                new(preview.Binding, selectedSkills, selectedGroups)), cancellationToken);
            if (!IsCreationSkillsReReviewStateCurrent(issued.Load.State)
                || canonical.Outcome != CharacterCreationFoundationOutcomes.Success
                || canonical.Value is not { CurrentPreview.CanConfirm: true } exact
                || !CreationSkillsReReviewPhoneAuthority.Equal(preview, exact))
                return RejectedSkills(CharacterCreationSkillsBlockers.PreviewDigestMismatch);
            string key = CreationSkillsReReviewPhoneAuthority.IdempotencyKey(exact);
            cancellationToken.ThrowIfCancellationRequested();
            issued.ConfirmationStarted = true;
            int entered = 0;
            CharacterCreationFoundationResult<CharacterCreationSkillsReceipt> result;
            try
            {
                // Core holds the exact original owner's synchronous lease through
                // the atomic save. An uncertain write is never automatically retried.
                result = await Task.Run(() =>
                {
                    Interlocked.Exchange(ref entered, 1);
                    return service.ConfirmReReview(owner,
                        new(exact.Binding, selectedSkills, selectedGroups, exact.PreviewDigest, key, true, true));
                }, cancellationToken);
            }
            catch (OperationCanceledException) when (Volatile.Read(ref entered) == 0)
            { issued.ConfirmationStarted = false; throw; }
            catch (Exception error) when (error is not OutOfMemoryException)
            { return new("outcome-unknown", null, null, ["creation-skills-confirm-outcome-unknown"]); }
            if (result is not { Outcome: CharacterCreationFoundationOutcomes.Success, Value: { } receipt })
                return new(result.Outcome, result.Value, null, result.Blockers);
            _skillReceipts.GetValue(receipt, _ => issued.Load.Display);
            try
            {
                var committed = await Task.Run(() => _ownerBoundSkillsService.Load(owner, new(receipt.WorkspaceId)));
                if (committed.Value is not { } saved
                    || !CreationSkillsReReviewPhoneAuthority.ReceiptMatches(receipt, exact, saved, key)
                    || !IsNativePersistenceOwnerCurrent(owner)
                    || _presenter is not IOwnerBoundWorkspaceRefreshPresenter bound)
                    return NeedsReopen();
                await bound.LoadAsync(owner, receipt.WorkspaceId, cancellationToken);
                if (!IsCreationSkillsReceiptCurrent(receipt)) return NeedsReopen();
                await SyncShellAsync(cancellationToken);
                if (!IsCreationSkillsReceiptCurrent(receipt)) return NeedsReopen();
                var refreshed = LoadCreationSkills();
                if (refreshed.Value is not { } current || !IsCreationSkillsStateCurrent(current)
                    || !CreationSkillsReReviewPhoneAuthority.ReceiptMatches(receipt, exact, current, key))
                    return NeedsReopen();
                NotifyChanged();
                return IsCreationSkillsReceiptCurrent(receipt)
                    ? new(CharacterCreationFoundationOutcomes.Success, receipt, current, []) : NeedsReopen();
            }
            catch (Exception error) when (error is not OutOfMemoryException) { return NeedsReopen(); }
            CreationSkillsPhoneConfirmResult NeedsReopen() => new(CharacterCreationFoundationOutcomes.Success,
                receipt, null, [CharacterCreationSkillsBlockers.PostCommitRefreshRequired]);
        }, cancellationToken);
    }
}
