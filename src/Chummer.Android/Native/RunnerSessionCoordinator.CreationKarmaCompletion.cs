using System.Runtime.CompilerServices;
using Chummer.Contracts.Characters;
using Chummer.Presentation;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed partial class RunnerSessionCoordinator
{
    private readonly ConditionalWeakTable<CharacterCreationStartingNuyenSource, CharacterCreationKarmaMetatypeQuote> _karmaCashSources = new();
    private readonly ConditionalWeakTable<CharacterCreationFinalizationReview, KarmaCompletionIntent> _karmaCompletions = new();
    private CharacterCreationFinalizationReview? _karmaCompletionReview;

    private sealed record KarmaCompletionIntent(CharacterCreationKarmaMetatypeQuote Foundation,
        CharacterOverviewState Original, CharacterCreationKarmaFinalizationConfirmRequest Command)
    {
        public bool Started { get; set; }
    }

    internal Task<CharacterCreationFoundationResult<CharacterCreationStartingNuyenSource>> LoadKarmaCompletionCashAsync(
        CharacterCreationKarmaMetatypeQuote foundation, CancellationToken ct, Func<bool> isCurrentPage)
        => WithWorkspaceActivationGateAsync(async () =>
        {
            if (!isCurrentPage() || !IsCreationKarmaPreviewCurrent(foundation)
                || !_karmaReviews.TryGetValue(foundation, out var issued)
                || issued.Original.DisplayOwnerContext is not { } owner || _ownerBoundKarmaService is not { } service)
                return KarmaStale<CharacterCreationStartingNuyenSource>();
            _karmaCompletionReview = null;
            var result = await Task.Run(() => service.LoadFinalizationStartingCash(owner, foundation.Binding, foundation.QuoteDigest), ct);
            ct.ThrowIfCancellationRequested();
            if (!isCurrentPage() || !IsCreationKarmaPreviewCurrent(foundation)) return KarmaStale<CharacterCreationStartingNuyenSource>();
            if (result.Value is { } source) _karmaCashSources.Add(source, foundation);
            return result;
        }, ct);

    internal Task<CharacterCreationFoundationResult<CharacterCreationFinalizationReview>> ReviewKarmaCompletionAsync(
        CharacterCreationKarmaMetatypeQuote foundation, CharacterCreationStartingNuyenSource source, int diceTotal,
        CancellationToken ct, Func<bool> isCurrentPage)
        => WithWorkspaceActivationGateAsync(async () =>
        {
            if (!isCurrentPage() || !IsCreationKarmaPreviewCurrent(foundation)
                || !_karmaCashSources.TryGetValue(source, out var sourceFoundation) || !ReferenceEquals(sourceFoundation, foundation)
                || !_karmaReviews.TryGetValue(foundation, out var issued)
                || issued.Original.DisplayOwnerContext is not { } owner || _ownerBoundKarmaService is not { } service)
                return KarmaStale<CharacterCreationFinalizationReview>();
            _karmaCompletionReview = null;
            var result = await Task.Run(() => service.ReviewFinalization(owner, foundation.Binding, foundation.QuoteDigest,
                diceTotal, source.AuthorityDigest), ct);
            ct.ThrowIfCancellationRequested();
            if (!isCurrentPage() || !IsCreationKarmaPreviewCurrent(foundation)) return KarmaStale<CharacterCreationFinalizationReview>();
            if (result is { Outcome: CharacterCreationFoundationOutcomes.Success, Value: { CanConfirm: true, Plan: { } plan } review })
            {
                if (review.Binding.WorkspaceId != foundation.Binding.WorkspaceId
                    || review.Binding.ContentRevision != foundation.Binding.ContentRevision
                    || review.Binding.SavedRevision != foundation.Binding.SavedRevision
                    || review.Binding.RawCharacterXmlDigest != foundation.Binding.RawCharacterXmlDigest
                    || review.Binding.AuxiliaryStateDigest != foundation.Binding.AuxiliaryStateDigest
                    || review.Binding.BuildMethod != CharacterCreationBuildMethods.Karma
                    || review.Blockers.Count != 0 || !review.RequiresExplicitConfirmation || plan.Binding != review.Binding
                    || review.Schema != CharacterCreationFinalizationSchemas.ReviewV1
                    || !CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(plan.PlanDigest)
                    || !CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(review.PreviewDigest))
                    return KarmaStale<CharacterCreationFinalizationReview>();
                var command = new CharacterCreationKarmaFinalizationConfirmRequest(new(review.Binding, review.PreviewDigest,
                    plan.PlanDigest, "karma-completion:" + Guid.NewGuid().ToString("N"), true), diceTotal);
                _karmaCompletions.Add(review, new(foundation, issued.Original, command));
                _karmaCompletionReview = review;
            }
            return result;
        }, ct);

    internal bool IsKarmaCompletionCurrent(CharacterCreationFinalizationReview review)
        => ReferenceEquals(_karmaCompletionReview, review) && _karmaCompletions.TryGetValue(review, out var issued)
           && !issued.Started && IsCreationKarmaPreviewCurrent(issued.Foundation);

    internal Task<CharacterCreationFoundationResult<CharacterCreationFinalizationReceipt>> ConfirmKarmaCompletionAsync(
        CharacterCreationFinalizationReview review, bool explicitlyConfirmed, CancellationToken ct, Func<bool> isCurrentPage)
    {
        if (!explicitlyConfirmed)
            return Task.FromResult(new CharacterCreationFoundationResult<CharacterCreationFinalizationReceipt>(
                CharacterCreationFoundationOutcomes.Blocked, null, [CharacterCreationFinalizationBlockers.ExplicitConfirmationRequired]));
        return WithWorkspaceActivationGateAsync(async () =>
        {
            if (!isCurrentPage() || !IsKarmaCompletionCurrent(review) || !_karmaCompletions.TryGetValue(review, out var issued)
                || issued.Original.DisplayOwnerContext is not { } owner || _ownerBoundKarmaService is not { } service)
                return KarmaStale<CharacterCreationFinalizationReceipt>();
            ct.ThrowIfCancellationRequested();
            issued.Started = true;
            int entered = 0;
            CharacterCreationFoundationResult<CharacterCreationFinalizationReceipt> result;
            try
            {
                result = await Task.Run(() =>
                {
                    Interlocked.Exchange(ref entered, 1);
                    return service.ConfirmFinalization(owner, issued.Command);
                }, ct);
            }
            catch (OperationCanceledException) when (Volatile.Read(ref entered) == 0)
            { issued.Started = false; throw; }
            catch (Exception error) when (error is not OutOfMemoryException)
            { return new("outcome-unknown", null, [KarmaOutcomeUnknown]); }
            // One admitted command only. A failed/unknown return never creates
            // another ID or repeats a mutation. Core owns atomic recovery.
            _karmaCompletionReview = null;
            _karmaCurrentState = null;
            _karmaCurrentReview = null;
            if (result is not { Outcome: CharacterCreationFoundationOutcomes.Success, Value: { } receipt }) return result;
            _finalizationReceipts.GetValue(receipt, _ => issued.Original);
            try
            {
                // Do not report cancellation after a known durable commit.
                if (ct.IsCancellationRequested || !isCurrentPage() || !CanDisplayCreationFinalizationReceipt(receipt)
                    || _presenter is not IOwnerBoundWorkspaceRefreshPresenter refresh) return NeedsReopen();
                await refresh.LoadAsync(owner, receipt.WorkspaceId, ct);
                if (ct.IsCancellationRequested || !isCurrentPage() || !IsCreationFinalizationReceiptCurrent(receipt)) return NeedsReopen();
                await SyncShellAsync(ct);
                if (ct.IsCancellationRequested || !isCurrentPage() || !IsCreationFinalizationReceiptCurrent(receipt)) return NeedsReopen();
                NotifyChanged();
                return result;
            }
            catch (Exception error) when (error is not OutOfMemoryException) { return NeedsReopen(); }
            CharacterCreationFoundationResult<CharacterCreationFinalizationReceipt> NeedsReopen()
                => new(result.Outcome, receipt, [CharacterCreationFinalizationBlockers.PostCommitReopenRequired]);
        }, ct);
    }
}
