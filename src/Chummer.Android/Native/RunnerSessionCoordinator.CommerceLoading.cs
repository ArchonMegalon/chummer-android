using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;
using Chummer.Presentation;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Chummer.Android.Native;

public sealed partial class RunnerSessionCoordinator
{
    private readonly SemaphoreSlim _commercePreparationGate = new(1, 1);
    private readonly CareerCommerceOwnerAdmission? _commerceOwnerAdmission;
    private readonly ConditionalWeakTable<object, IssuedCommerceSnapshot> _commerceSnapshots = new();
    private sealed record IssuedCommerceSnapshot(CharacterOverviewState Overview, string Identity);

    // A retained button owns the exact rendered snapshot, not whichever account
    // happens to be active when its dialog returns. Freeze value identity too:
    // component lists in Core records are not necessarily immutable collections.
    private static string CommerceIdentity(object snapshot) => snapshot switch
    {
        Sr5CareerCyberwarePurchaseSnapshot cyber => JsonSerializer.Serialize(new
        {
            cyber.WorkspaceId, cyber.Preparation?.ContentRevision, cyber.Preparation?.CharacterDigest,
            cyber.Preparation?.CatalogDigest, cyber.Selection, cyber.Checkpoint
        }),
        Sr5CareerCustomDrugRecipeSnapshot drug => JsonSerializer.Serialize(new
        {
            drug.WorkspaceId, drug.Preparation?.ContentRevision, drug.Preparation?.CharacterDigest,
            drug.Preparation?.CatalogDigest, drug.Preparation?.RulesDigest, drug.Selection, drug.Checkpoint
        }),
        _ => throw new InvalidOperationException("Unknown commerce snapshot.")
    };

    private T IssueCommerceSnapshot<T>(T snapshot, CharacterOverviewState overview) where T : class
    {
        _commerceSnapshots.Add(snapshot, new(overview, CommerceIdentity(snapshot)));
        return snapshot;
    }

    private CharacterOverviewState RequireCommerceIssuance(object expected)
    {
        if (!_commerceSnapshots.TryGetValue(expected, out var issued)
            || !IsCommerceDisplayCurrent(issued.Overview)
            || issued.Identity != CommerceIdentity(expected))
            throw new InvalidOperationException("The commerce page changed. Open the wizard again.");
        return issued.Overview;
    }

    private T LoadCommerceCurrent<T>(Func<CharacterWorkspaceId, T?> load, Func<T, bool> valid,
        Func<CharacterWorkspaceId, T> blocked) where T : class
    {
        CharacterOverviewState original = State;
        CharacterWorkspaceId workspace = original.WorkspaceId ?? default;
        if (!IsCommerceDisplayCurrent(original) || original.DisplayOwnerContext is not { } stamp
            || _commerceOwnerAdmission is null || !_commerceOwnerAdmission.TryEnter(stamp, workspace, out var scope))
            return blocked(workspace);
        using (scope)
        {
            if (!IsCommerceDisplayCurrent(original)) return blocked(workspace);
            T? snapshot = load(workspace);
            return snapshot is not null && valid(snapshot)
                ? IssueCommerceSnapshot(snapshot, original) : blocked(workspace);
        }
    }

    private T WithCommerceSnapshot<T>(T expected, Func<CharacterWorkspaceId, T> load,
        Func<T, bool> ready, Func<CharacterWorkspaceId, T> operation) where T : class
    {
        CharacterOverviewState original = RequireCommerceIssuance(expected);
        CharacterWorkspaceId workspace = original.WorkspaceId!.Value;
        if (_commerceOwnerAdmission is null || original.DisplayOwnerContext is not { } stamp
            || !_commerceOwnerAdmission.TryEnter(stamp, workspace, out var scope))
            throw new InvalidOperationException("The commerce owner changed.");
        using (scope)
        {
            _ = RequireCommerceIssuance(expected);
            T current = load(workspace);
            if (!ready(current) || CommerceIdentity(current) != CommerceIdentity(expected))
                throw new InvalidOperationException("The commerce draft or receipt changed. Reload the wizard.");
            return IssueCommerceSnapshot(operation(workspace), original);
        }
    }

    private Sr5CareerCyberwarePurchaseSnapshot WithCyberwareSnapshot(
        Sr5CareerCyberwarePurchaseSnapshot expected, Func<CharacterWorkspaceId, Sr5CareerCyberwarePurchaseSnapshot> action)
        => WithCommerceSnapshot(expected, id => _careerCyberwarePurchaseService!.Load(id), snapshot => snapshot.IsReady, action);

    private Sr5CareerCustomDrugRecipeSnapshot WithDrugSnapshot(
        Sr5CareerCustomDrugRecipeSnapshot expected, Func<CharacterWorkspaceId, Sr5CareerCustomDrugRecipeSnapshot> action)
        => WithCommerceSnapshot(expected, id => _careerCustomDrugRecipeService!.Load(id), snapshot => snapshot.IsReady, action);

    private async Task RefreshCommerceReceiptAsync(CharacterOverviewState original, CancellationToken ct)
    {
        // The durable receipt remains true even if display refresh fails. Never
        // report cancellation as a canceled mutation or refresh another account.
        try
        {
            if (!CommerceOwnerAndWorkspaceCurrent(original)
                || _presenter is not IOwnerBoundWorkspaceRefreshPresenter refresh) return;
            await refresh.LoadAsync(original.DisplayOwnerContext!.Value, original.WorkspaceId!.Value, ct);
            if (!CommerceOwnerAndWorkspaceCurrent(original)) return;
            await SyncShellAsync(ct);
            if (CommerceOwnerAndWorkspaceCurrent(original)) NotifyChanged();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Reopening the wizard reads the durable result; it never replays.
        }
    }

    private bool CommerceOwnerAndWorkspaceCurrent(CharacterOverviewState original)
        => !_disposed && State.WorkspaceId == original.WorkspaceId
            && State.DisplayOwnerContext == original.DisplayOwnerContext
            && State.Session.OwnerContext == original.DisplayOwnerContext
            && _shellPresenter.State.OwnerContext == original.DisplayOwnerContext
            && TryCaptureWorkspaceOwner(out var owner) && owner == original.DisplayOwnerContext;

    internal Task<Sr5CareerCyberwarePurchaseSnapshot> LoadCareerCyberwarePurchaseAsync(
        CancellationToken cancellationToken, Func<bool> isCurrentPage)
        => LoadCommerceAsync(
            id => _careerCyberwarePurchaseService?.CaptureLoad(id),
            captured => _careerCyberwarePurchaseService!.PrepareLoad(captured),
            (captured, prepared) => _careerCyberwarePurchaseService!.CompleteLoad(captured, prepared),
            id => Sr5CareerCyberwarePurchaseSnapshot.Blocked(id, CharacterCyberwarePurchaseBlockers.StaleRevision),
            captured => captured.ContentRevision == State.ContentRevision
                && captured.SavedRevision == State.SavedRevision,
            cancellationToken, isCurrentPage);

    internal Task<Sr5CareerCustomDrugRecipeSnapshot> LoadCareerCustomDrugRecipeAsync(
        CancellationToken cancellationToken, Func<bool> isCurrentPage)
        => LoadCommerceAsync(
            id => _careerCustomDrugRecipeService?.CaptureLoad(id),
            captured => _careerCustomDrugRecipeService!.PrepareLoad(captured),
            (captured, prepared) => _careerCustomDrugRecipeService!.CompleteLoad(captured, prepared),
            id => Sr5CareerCustomDrugRecipeSnapshot.Blocked(id, CharacterCustomDrugBlockers.StaleRevision),
            captured => captured.ContentRevision == State.ContentRevision
                && captured.SavedRevision == State.SavedRevision,
            cancellationToken, isCurrentPage);

    private async Task<TResult> LoadCommerceAsync<TCaptured, TPrepared, TResult>(
        Func<CharacterWorkspaceId, TCaptured?> capture, Func<TCaptured, TPrepared> prepare,
        Func<TCaptured, TPrepared, TResult> complete, Func<CharacterWorkspaceId, TResult> blocked,
        Func<TCaptured, bool> matchesRevision, CancellationToken cancellationToken, Func<bool> isCurrentPage)
        where TCaptured : class
        where TResult : class
    {
        // State, account and store access stays on the caller/UI thread. No
        // owner lease crosses an await or makes an account switch wait on XML.
        CharacterOverviewState original = State;
        CharacterWorkspaceId workspace = original.WorkspaceId ?? default;
        if (!IsCommerceDisplayCurrent(original) || !isCurrentPage()
            || _commerceOwnerAdmission is null
            || original.DisplayOwnerContext is not { IsValid: true } stamp)
            return blocked(workspace);
        cancellationToken.ThrowIfCancellationRequested();
        TCaptured? captured;
        if (!_commerceOwnerAdmission.TryEnter(stamp, workspace, out var lease)) return blocked(workspace);
        using (lease)
        {
            if (!IsCommerceDisplayCurrent(original) || !isCurrentPage()) return blocked(workspace);
            captured = capture(workspace);
            if (captured is null || !matchesRevision(captured)) return blocked(workspace);
        }

        TPrepared prepared;
        await _commercePreparationGate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCommerceDisplayCurrent(original) || !isCurrentPage()) return blocked(workspace);
            prepared = await Task.Run(() => prepare(captured), cancellationToken);
        }
        finally { _commercePreparationGate.Release(); }
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsCommerceDisplayCurrent(original) || !isCurrentPage()
            || !_commerceOwnerAdmission.TryEnter(stamp, workspace, out lease)) return blocked(workspace);
        using (lease)
        {
            if (!IsCommerceDisplayCurrent(original) || !isCurrentPage()) return blocked(workspace);
            // Service double-reads actual bytes/revisions before touching a
            // checkpoint. Read the latest draft, never overwrite a newer edit
            // with an old checkpoint captured before the background calculation.
            return IssueCommerceSnapshot(complete(captured, prepared), original);
        }
    }

    private bool IsCommerceDisplayCurrent(CharacterOverviewState original)
    {
        CharacterOverviewState current = State;
        return !_disposed && original.WorkspaceId is not null
            && original.Profile?.Created == true && current.Profile?.Created == true
            && string.Equals(current.Rules?.GameEdition, "SR5", StringComparison.OrdinalIgnoreCase)
            && current.WorkspaceId == original.WorkspaceId
            && current.ContentRevision == original.ContentRevision
            && current.SavedRevision == original.SavedRevision
            && current.ContentRevision == current.SavedRevision && !current.IsDirty && !current.IsBusy
            && current.Error is null && current.ConflictState is null
            && current.DisplayOwnerContext == original.DisplayOwnerContext
            && current.Session.OwnerContext == original.DisplayOwnerContext
            && _shellPresenter.State.OwnerContext == original.DisplayOwnerContext
            && TryCaptureWorkspaceOwner(out var owner) && owner == original.DisplayOwnerContext;
    }
}
