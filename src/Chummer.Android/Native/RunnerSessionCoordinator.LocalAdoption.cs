using Chummer.Application.Workspaces;
using Chummer.Contracts.Owners;

namespace Chummer.Android.Native;

public sealed partial class RunnerSessionCoordinator
{
    private readonly AndroidLocalRunnerAdoptionService? _localRunnerAdoption;
    private bool _localRunnerAdoptionRecoveryPending;

    internal bool CanAdoptLocalRunner => _localRunnerAdoption is not null
        && !_localRunnerAdoptionRecoveryPending && TryCaptureWorkspaceOwner(out var owner)
        && owner is { IsValid: true } stamp && !stamp.Owner.UsesLocalSingleUserValue
        && IsWorkspaceOwnerInitialized();
    internal bool HasPendingLocalRunnerAdoption => _localRunnerAdoptionRecoveryPending;

    private async Task<bool> RecoverLocalRunnerAdoptionsAsync(CancellationToken ct)
    {
        if (_localRunnerAdoption is null) return true;
        if (!TryCaptureWorkspaceOwner(out var owner) || owner is null) return false;
        try
        {
            _localRunnerAdoptionRecoveryPending = !await _localRunnerAdoption.RecoverPendingAsync(owner.Value, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is IOException or InvalidOperationException or System.Text.Json.JsonException)
        {
            _localRunnerAdoptionRecoveryPending = true;
        }
        if (_localRunnerAdoptionRecoveryPending)
            _notice = PhoneStrings.Get("LocalRunnerAdoptionPending",
                "Runner transfer is not finished. Your original data is kept. Retry to finish the same transfer.");
        return !_localRunnerAdoptionRecoveryPending;
    }

    internal async Task RetryLocalRunnerAdoptionsAsync(CancellationToken ct = default)
    {
        _workspaceOwnerInitializationPending = !await InitializeWorkspaceOwnerAsync(ct);
        if (!_workspaceOwnerInitializationPending)
            _notice = PhoneStrings.Get("LocalRunnerAdoptionRecovered", "Runner transfer finished. Your runners are ready.");
        NotifyChanged();
    }

    internal async Task<IReadOnlyList<NativeLocalRunnerCandidate>> ListLocalRunnerCandidatesAsync(CancellationToken ct)
    {
        if (!CanAdoptLocalRunner || !TryCaptureWorkspaceOwner(out var owner) || owner is null)
            return [];
        var result = await _localRunnerAdoption!.ListAsync(owner.Value, ct);
        return CanAdoptLocalRunner && IsNativePersistenceOwnerCurrent(owner.Value) ? result : [];
    }

    internal Task<NativeLocalRunnerAdoptionReview?> ReviewLocalRunnerAdoptionAsync(
        NativeLocalRunnerCandidate candidate, CancellationToken ct = default)
        => CanAdoptLocalRunner && IsNativePersistenceOwnerCurrent(candidate.Owner)
            ? _localRunnerAdoption!.ReviewAsync(candidate.Owner, candidate.WorkspaceId, ct)
            : Task.FromResult<NativeLocalRunnerAdoptionReview?>(null);

    internal Task<bool> ConfirmLocalRunnerAdoptionAsync(NativeLocalRunnerAdoptionReview review,
        bool explicitlyConfirmed, CancellationToken ct = default)
        => WithWorkspaceActivationGateAsync(async () =>
        {
            if (!explicitlyConfirmed || !CanAdoptLocalRunner || !IsNativePersistenceOwnerCurrent(review.Owner)) return false;
            var result = await _localRunnerAdoption!.ConfirmAsync(review.Owner, review, true, ct);
            if (result.Outcome == WorkspaceLocalAdoptionOutcome.RecoveryRequired)
            {
                _localRunnerAdoptionRecoveryPending = true;
                _workspaceOwnerInitializationPending = true;
                _notice = PhoneStrings.Get("LocalRunnerAdoptionPending",
                    "Runner transfer is not finished. Your original data is kept. Retry to finish the same transfer.");
                NotifyChanged();
                return false;
            }
            if (result.Outcome is not (WorkspaceLocalAdoptionOutcome.Applied or WorkspaceLocalAdoptionOutcome.Recovered)
                || !IsNativePersistenceOwnerCurrent(review.Owner)) return false;
            // The claim and all private book placements are complete. Refresh
            // the actual account roster before activating the same workspace.
            // Do not import XML, manufacture a restore receipt or generate a book.
            await _shellPresenter.InitializeAsync(ct);
            await _presenter.InitializeAsync(ct);
            if (!IsWorkspaceOwnerInitialized() || !IsNativePersistenceOwnerCurrent(review.Owner)) return false;
            AdvanceAfterRunRewardSelection();
            await _presenter.SwitchWorkspaceAsync(review.Core.WorkspaceId, ct);
            await SyncShellAsync(ct);
            RestorePlayState();
            _notice = PhoneStrings.Get("LocalRunnerAdoptionComplete",
                "Runner and book transferred to this account on this device.");
            NotifyChanged();
            return IsNativePersistenceOwnerCurrent(review.Owner) && WorkspaceIsActive(State, review.Core.WorkspaceId);
        }, ct);
}
