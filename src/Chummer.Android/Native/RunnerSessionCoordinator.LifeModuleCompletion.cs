using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed partial class RunnerSessionCoordinator
{
    internal LifeModuleCompletionDraftStore? LifeModuleInputDrafts { get; }
    private readonly IOwnerBoundCharacterCreationLifeModuleFinalizationService? _lifeModuleFinalizationService;
    private readonly ConditionalWeakTable<CharacterCreationFoundationState, CharacterOverviewState> _lifeCompletionStates = new();
    private readonly ConditionalWeakTable<CharacterCreationFoundationFinalizationPreview, LifeCompletionIntent> _lifeCompletionReviews = new();
    private readonly ConditionalWeakTable<CharacterCreationFoundationFinalizationReceipt, CharacterOverviewState> _lifeCompletionReceipts = new();
    private CharacterCreationFoundationState? _lifeCompletionState;
    private CharacterCreationFoundationFinalizationPreview? _lifeCompletionReview;

    private sealed record LifeCompletionIntent(CharacterCreationFoundationState State, CharacterOverviewState Original,
        CharacterCreationFoundationFinalizationPreviewRequest Request, string PreviewSnapshotDigest)
    {
        public bool Started { get; set; }
    }

    internal bool CanOpenLifeModuleCompletion()
        => _lifeModuleFinalizationService is not null && LifeCompletionDisplayCurrent(State)
           && State.CreationFoundation?.PendingDraft is { ModuleSelectionFinished: true, CharacterEffectsApplied: false };

    private async Task RefreshLifeModuleDashboardAfterSaveAsync(
        CharacterOverviewState original, WorkspaceSaveReceipt receipt, CancellationToken ct)
    {
        // Save advances SavedRevision without changing the runner's content.
        // Reload the display through its existing original-owner path, rather
        // than rewriting Core bindings or leaving the next wizard disabled.
        if (ct.IsCancellationRequested || receipt.Id != original.WorkspaceId
            || receipt.ContentRevision != original.ContentRevision
            || receipt.SavedRevision != receipt.ContentRevision
            || !IsNativePersistenceViewCurrent(original, receipt.ContentRevision)
            || original.CreationWizard is not
                { RulesetId: RulesetDefaults.Sr5, BuildMethod: CharacterCreationBuildMethods.LifeModules, CharacterCreated: false }
            || State.CreationFoundation is not { } foundation
            || foundation.Binding.SavedRevision == receipt.SavedRevision
            || original.DisplayOwnerContext is not { IsValid: true } owner
            || _presenter is not IOwnerBoundWorkspaceRefreshPresenter refresh) return;
        try
        {
            await refresh.LoadAsync(owner, receipt.Id, ct);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // The original Save is already committed. Never replay it because
            // the following read failed; stale display admission stays closed.
        }
    }

    // Display only: use the same captured Core foundation as the current owner
    // and workspace. Opening a wizard still reloads and validates its authority.
    internal bool IsLifeModuleDashboardCurrent(CharacterOverviewState original)
        => LifeCompletionDisplayCurrent(original)
           && ReferenceEquals(State.CreationWizard, original.CreationWizard)
           && ReferenceEquals(State.CreationFoundation, original.CreationFoundation)
           && original.CreationFoundation is
           { RulesetId: RulesetDefaults.Sr5, BuildMethod: CharacterCreationBuildMethods.LifeModules, CharacterCreated: false } foundation
           && foundation.Binding.WorkspaceId == original.WorkspaceId
           && foundation.Binding.ContentRevision == original.ContentRevision
           && foundation.Binding.SavedRevision == original.SavedRevision
           && foundation.Binding.RawCharacterXmlDigest == original.CreationWizard!.ContentDigest
           && foundation.Binding.SourceDigest == original.CreationWizard.SourceDigest
           && !foundation.Binding.SourceFilterApplied
           && foundation.PendingDraft?.CharacterEffectsApplied != true
           && CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(foundation.SnapshotDigest)
           && CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(foundation.Binding.SourceDigest);

    private bool LifeCompletionDisplayCurrent(CharacterOverviewState original)
        => original.Profile?.Created == false && IsNativeEditDisplayCurrent(original)
           && original.CreationWizard is
           { BuildMethod: CharacterCreationBuildMethods.LifeModules, RulesetId: RulesetDefaults.Sr5, CharacterCreated: false } wizard
           && wizard.WorkspaceId == original.WorkspaceId?.Value && wizard.WorkspaceRevision == original.ContentRevision;

    private static CharacterCreationFoundationResult<T> LifeCompletionStale<T>() where T : class
        => new(CharacterCreationFoundationOutcomes.Conflict, null, [CharacterCreationFoundationBlockers.StaleWorkspaceRevision]);

    internal bool IsLifeModuleCompletionStateCurrent(CharacterCreationFoundationState state)
        => ReferenceEquals(_lifeCompletionState, state) && _lifeCompletionStates.TryGetValue(state, out var original)
           && LifeCompletionDisplayCurrent(original) && state.Binding.WorkspaceId == original.WorkspaceId
           && state.Binding.ContentRevision == original.ContentRevision && state.Binding.SavedRevision == original.SavedRevision;

    internal Task<CharacterCreationFoundationResult<CharacterCreationFoundationState>> LoadLifeModuleCompletionAsync(
        CancellationToken ct = default, Func<bool>? isCurrentPage = null)
    {
        var original = State;
        return WithWorkspaceActivationGateAsync(async () =>
        {
            if (isCurrentPage?.Invoke() == false || !LifeCompletionDisplayCurrent(original)
                || _lifeModuleFinalizationService is not { } service || original.DisplayOwnerContext is not { } owner
                || original.WorkspaceId is not { } id) return LifeCompletionStale<CharacterCreationFoundationState>();
            _lifeCompletionState = null;
            _lifeCompletionReview = null;
            var result = await Task.Run(() => service.Load(owner, id), ct);
            ct.ThrowIfCancellationRequested();
            if (isCurrentPage?.Invoke() == false || !LifeCompletionDisplayCurrent(original))
                return LifeCompletionStale<CharacterCreationFoundationState>();
            if (result.Value is not { } state) return result;
            if (state.Schema != CharacterCreationFoundationSchemas.SnapshotV1 || state.CharacterCreated
                || state.RulesetId != RulesetDefaults.Sr5 || state.BuildMethod != CharacterCreationBuildMethods.LifeModules
                || state.PendingDraft is not { ModuleSelectionFinished: true, CharacterEffectsApplied: false }
                || state.AuthorityBlockers.Count != 0 || state.Binding.WorkspaceId != id
                || state.Binding.ContentRevision != original.ContentRevision || state.Binding.SavedRevision != original.SavedRevision
                || !CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(state.SnapshotDigest))
                return LifeCompletionStale<CharacterCreationFoundationState>();
            var issuer = _lifeCompletionStates.GetValue(state, _ => original);
            if (!LifeCompletionDisplayCurrent(issuer)) return LifeCompletionStale<CharacterCreationFoundationState>();
            _lifeCompletionState = state;
            return result;
        }, ct);
    }

    internal Task<CharacterCreationFoundationResult<CharacterCreationFoundationFinalizationPreview>> PreviewLifeModuleCompletionAsync(
        CharacterCreationFoundationState state, CharacterCreationFoundationFinalizationPreviewRequest request,
        CancellationToken ct = default, Func<bool>? isCurrentPage = null)
    {
        // Freeze before scheduling. Pending phone selections are not authority.
        var frozen = JsonSerializer.SerializeToElement(request).Deserialize<CharacterCreationFoundationFinalizationPreviewRequest>()!;
        return WithWorkspaceActivationGateAsync(async () =>
        {
            if (isCurrentPage?.Invoke() == false || !IsLifeModuleCompletionStateCurrent(state)
                || !_lifeCompletionStates.TryGetValue(state, out var original) || original.DisplayOwnerContext is not { } owner
                || _lifeModuleFinalizationService is not { } service
                || JsonSerializer.Serialize(frozen.Binding) != JsonSerializer.Serialize(state.Binding)
                || frozen.DraftRevision != state.PendingDraft!.DraftRevision || frozen.DraftDigest != state.PendingDraft.DraftDigest)
                return LifeCompletionStale<CharacterCreationFoundationFinalizationPreview>();
            _lifeCompletionReview = null;
            var inspected = await Task.Run(() =>
            {
                var projected = service.Preview(owner, frozen);
                // Catalog-bearing results can be large. Retain their immutable
                // display identity off the UI thread, not during a page render.
                return (Result: projected, Digest: projected.Value is null ? string.Empty
                    : LifeCompletionDisplayDigest(projected.Value));
            }, ct);
            var result = inspected.Result;
            ct.ThrowIfCancellationRequested();
            if (isCurrentPage?.Invoke() == false || !IsLifeModuleCompletionStateCurrent(state))
                return LifeCompletionStale<CharacterCreationFoundationFinalizationPreview>();
            if (result.Value is not { } preview) return result;
            if (preview.Schema != CharacterCreationFoundationSchemas.FinalizationPreviewV1
                || JsonSerializer.Serialize(preview.Binding) != JsonSerializer.Serialize(state.Binding)
                || preview.CharacterCreated || preview.CharacterEffectsApplied || !preview.RequiresExplicitConfirmation
                || !CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(preview.PreviewDigest))
                return LifeCompletionStale<CharacterCreationFoundationFinalizationPreview>();
            // Incomplete selections may expose typed catalogs and blockers, but
            // only the exact full Core plan may be confirmed.
            _lifeCompletionReviews.Add(preview, new(state, original, frozen, inspected.Digest));
            _lifeCompletionReview = preview;
            return result;
        }, ct);
    }

    internal bool IsLifeModuleCompletionPreviewCurrent(CharacterCreationFoundationFinalizationPreview preview)
        => ReferenceEquals(_lifeCompletionReview, preview) && _lifeCompletionReviews.TryGetValue(preview, out var intent)
           && !intent.Started && IsLifeModuleCompletionStateCurrent(intent.State);

    private static string LifeCompletionDisplayDigest(CharacterCreationFoundationFinalizationPreview preview)
        => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(preview)));

    internal bool IsLifeModuleCompletionReceiptCurrent(CharacterCreationFoundationFinalizationReceipt receipt)
        => _lifeCompletionReceipts.TryGetValue(receipt, out var original) && IsPrerequisiteOriginalOwnerVisible(original)
           && State.Profile?.Created == true && State.ContentRevision == receipt.ContentRevision && State.SavedRevision == receipt.SavedRevision;

    internal Task<CharacterCreationFoundationResult<CharacterCreationFoundationFinalizationReceipt>> ConfirmLifeModuleCompletionAsync(
        CharacterCreationFoundationFinalizationPreview preview, bool explicitlyConfirmed,
        CancellationToken ct = default, Func<bool>? isCurrentPage = null)
    {
        if (!explicitlyConfirmed) return Task.FromResult(new CharacterCreationFoundationResult<CharacterCreationFoundationFinalizationReceipt>(
            CharacterCreationFoundationOutcomes.Blocked, null, [CharacterCreationFinalizationBlockers.ExplicitConfirmationRequired]));
        return WithWorkspaceActivationGateAsync(async () =>
        {
            if (isCurrentPage?.Invoke() == false || !IsLifeModuleCompletionPreviewCurrent(preview)
                || preview is not { CanApply: true, CanConfirm: true, FinalizationBlocked.Count: 0, FinalizationPlan: { } plan }
                || !_lifeCompletionReviews.TryGetValue(preview, out var intent) || intent.Original.DisplayOwnerContext is not { } owner
                || _lifeModuleFinalizationService is not { } service) return LifeCompletionStale<CharacterCreationFoundationFinalizationReceipt>();
            var input = intent.Request;
            var command = new CharacterCreationFoundationFinalizationConfirmRequest(input.Binding, input.DraftRevision,
                input.DraftDigest, preview.PreviewDigest, true)
            {
                QualityInstanceValues = input.QualityInstanceValues, AttributePurchases = input.AttributePurchases,
                TalentSelection = input.TalentSelection, SkillSelection = input.SkillSelection,
                KarmaResourceInvestment = input.KarmaResourceInvestment, GearSelection = input.GearSelection,
                LifestyleSelection = input.LifestyleSelection, StartingLifestyleId = input.StartingLifestyleId,
                ContactSelection = input.ContactSelection, MagicSelection = input.MagicSelection, StartingNuyenDiceTotal = input.StartingNuyenDiceTotal
            };
            ct.ThrowIfCancellationRequested();
            intent.Started = true;
            int entered = 0;
            CharacterCreationFoundationResult<CharacterCreationFoundationFinalizationReceipt> result;
            try
            {
                result = await Task.Run(() =>
                {
                    Interlocked.Exchange(ref entered, 1);
                    if (intent.PreviewSnapshotDigest != LifeCompletionDisplayDigest(preview))
                        return LifeCompletionStale<CharacterCreationFoundationFinalizationReceipt>();
                    return service.Confirm(owner, command);
                }, ct);
            }
            catch (OperationCanceledException) when (Volatile.Read(ref entered) == 0) { intent.Started = false; throw; }
            catch (Exception error) when (error is not OutOfMemoryException)
            { return new("outcome-unknown", null, ["creation-life-module-completion-outcome-unknown"]); }
            _lifeCompletionReview = null;
            _lifeCompletionState = null;
            if (result is not { Outcome: CharacterCreationFoundationOutcomes.Success, Value: { } receipt }) return result;
            if (receipt.WorkspaceId != input.Binding.WorkspaceId || receipt.PreviousContentRevision != input.Binding.ContentRevision
                || receipt.ContentRevision != receipt.PreviousContentRevision + 1 || receipt.SavedRevision != receipt.ContentRevision
                || receipt.PreviewDigest != preview.PreviewDigest || receipt.RawCharacterXmlDigest != plan.ExpectedResultRawCharacterXmlDigest
                || !receipt.CharacterCreated || !receipt.CharacterEffectsApplied)
                return new("outcome-unknown", null, ["creation-life-module-completion-receipt-mismatch"]);
            _lifeCompletionReceipts.Add(receipt, intent.Original);
            try
            {
                if (ct.IsCancellationRequested || isCurrentPage?.Invoke() == false || !IsPrerequisiteOriginalOwnerVisible(intent.Original)
                    || _presenter is not IOwnerBoundWorkspaceRefreshPresenter refresh) return NeedsReopen();
                await refresh.LoadAsync(owner, receipt.WorkspaceId, ct);
                if (ct.IsCancellationRequested || isCurrentPage?.Invoke() == false || !IsLifeModuleCompletionReceiptCurrent(receipt)) return NeedsReopen();
                await SyncShellAsync(ct);
                if (ct.IsCancellationRequested || isCurrentPage?.Invoke() == false || !IsLifeModuleCompletionReceiptCurrent(receipt)) return NeedsReopen();
                NotifyChanged();
                return result;
            }
            catch (Exception error) when (error is not OutOfMemoryException) { return NeedsReopen(); }
            CharacterCreationFoundationResult<CharacterCreationFoundationFinalizationReceipt> NeedsReopen()
                => new(result.Outcome, receipt, [CharacterCreationFinalizationBlockers.PostCommitReopenRequired]);
        }, ct);
    }
}
