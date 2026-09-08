using Chummer.Application.Characters;
using Chummer.Contracts.Characters;

namespace Chummer.Android.Native;

public sealed partial class RunnerSessionCoordinator
{
    internal CharacterCreationFoundationResult<CharacterCreationSkillsReReviewState> LoadCreationSkillsReReview()
    {
        var before = State;
        if (_creationSkillsService is not ICharacterCreationSkillsReReviewService service
            || before.Profile?.Created != false || before.WorkspaceId is not { } id)
            return new(CharacterCreationFoundationOutcomes.Blocked, null, [CharacterCreationSkillsReReviewSchemas.Unavailable]);
        var result = service.LoadReReview(new(id));
        return result.Value is { } state
            && (!CreationSkillsReReviewPhoneAuthority.Matches(state, before)
                || !CreationSkillsReReviewPhoneAuthority.Matches(state, State))
            ? new(CharacterCreationFoundationOutcomes.Conflict, null, [CharacterCreationSkillsReReviewSchemas.Stale])
            : result;
    }

    internal CharacterCreationFoundationResult<CharacterCreationSkillsReReviewPreview> PreviewCreationSkillsReReview(
        CharacterCreationSkillsReReviewBinding binding,
        IReadOnlyList<CharacterCreationSkillAllocation> skills, IReadOnlyList<CharacterCreationSkillGroupAllocation> groups)
    {
        var loaded = LoadCreationSkillsReReview();
        if (_creationSkillsService is not ICharacterCreationSkillsReReviewService service
            || loaded.Value is not { } state || !CreationSkillsReReviewPhoneAuthority.Equal(state.Binding, binding))
            return new(CharacterCreationFoundationOutcomes.Conflict, null, [CharacterCreationSkillsReReviewSchemas.Stale]);
        var result = service.PreviewReReview(new(binding, skills.ToArray(), groups.ToArray()));
        return !CreationSkillsReReviewPhoneAuthority.Matches(state, State)
            || result.Value is { } preview && !CreationSkillsReReviewPhoneAuthority.ValidPreview(state, preview, skills, groups)
            ? new(CharacterCreationFoundationOutcomes.Conflict, null, [CharacterCreationSkillsReReviewSchemas.Stale])
            : result;
    }

    internal Task<CreationSkillsPhoneConfirmResult> ConfirmCreationSkillsReReviewAsync(
        CharacterCreationSkillsReReviewPreview preview,
        IReadOnlyList<CharacterCreationSkillAllocation> skills, IReadOnlyList<CharacterCreationSkillGroupAllocation> groups,
        bool explicitlyReviewed, CancellationToken cancellationToken = default)
    {
        var selectedSkills = skills.ToArray();
        var selectedGroups = groups.ToArray();
        return WithWorkspaceActivationGateAsync(async () =>
        {
            // All source resolution, disk reads and the atomic commit stay off
            // the UI thread. Cancellation after commit cannot disguise success.
            var confirmed = await Task.Run(() => ConfirmSkillsReReviewCore(
                preview, selectedSkills, selectedGroups, explicitlyReviewed), cancellationToken);
            if (confirmed.Receipt is not { } receipt || confirmed.RefreshedState is not { } committed
                || confirmed.Outcome != CharacterCreationFoundationOutcomes.Success) return confirmed;
            try
            {
                await _presenter.LoadAsync(receipt.WorkspaceId, cancellationToken);
                await SyncShellAsync(cancellationToken);
                if (!CreationSkillsPhoneAuthority.IsReady(committed, State))
                    return CommittedSkillsRefreshRequired(receipt, committed, []);
            }
            catch (Exception)
            {
                return CommittedSkillsRefreshRequired(receipt, committed, []);
            }
            NotifyChanged();
            return confirmed;
        }, cancellationToken);
    }

    private CreationSkillsPhoneConfirmResult ConfirmSkillsReReviewCore(
        CharacterCreationSkillsReReviewPreview preview,
        IReadOnlyList<CharacterCreationSkillAllocation> skills, IReadOnlyList<CharacterCreationSkillGroupAllocation> groups,
        bool explicitlyReviewed)
    {
        if (!explicitlyReviewed)
            return new(CharacterCreationFoundationOutcomes.Invalid, null, null,
                [CharacterCreationSkillsReReviewSchemas.ExplicitReviewRequired]);
        var loaded = LoadCreationSkillsReReview();
        if (_creationSkillsService is not ICharacterCreationSkillsReReviewService service
            || loaded.Value is not { } state
            || !CreationSkillsReReviewPhoneAuthority.ValidPreview(state, preview, skills, groups)
            || !preview.CurrentPreview.CanConfirm || preview.CurrentPreview.Blockers.Count != 0)
            return new(CharacterCreationFoundationOutcomes.Conflict, null, null, [CharacterCreationSkillsReReviewSchemas.Stale]);
        var reprojection = service.PreviewReReview(new(preview.Binding, skills, groups));
        if (reprojection.Outcome != CharacterCreationFoundationOutcomes.Success || reprojection.Value is not { } canonical
            || !canonical.CurrentPreview.CanConfirm || !CreationSkillsReReviewPhoneAuthority.Equal(preview, canonical)
            || !CreationSkillsReReviewPhoneAuthority.Matches(state, State))
            return new(CharacterCreationFoundationOutcomes.Conflict, null, null, [CharacterCreationSkillsBlockers.PreviewDigestMismatch]);
        string key = CreationSkillsReReviewPhoneAuthority.IdempotencyKey(canonical);
        var result = service.ConfirmReReview(new(canonical.Binding, skills, groups, canonical.PreviewDigest, key, true, true));
        if (result.Outcome != CharacterCreationFoundationOutcomes.Success || result.Value is not { } receipt)
            return new(result.Outcome, result.Value, null, result.Blockers);
        CharacterCreationFoundationResult<CharacterCreationSkillsState> committed;
        try { committed = _creationSkillsService.Load(new(receipt.WorkspaceId)); }
        catch (Exception)
        {
            return new(CharacterCreationFoundationOutcomes.Success, receipt, null,
                [CharacterCreationSkillsBlockers.PostCommitRefreshRequired]);
        }
        if (committed.Value is not { } current
            || !CreationSkillsReReviewPhoneAuthority.ReceiptMatches(receipt, canonical, current, key))
            // A receipt means a write may already be durable. The page must not
            // offer another confirmation when refresh/observation fails.
            return new(CharacterCreationFoundationOutcomes.Success, receipt, committed.Value,
                [CharacterCreationSkillsBlockers.PostCommitRefreshRequired]);
        return new(CharacterCreationFoundationOutcomes.Success, receipt, current, []);
    }
}
