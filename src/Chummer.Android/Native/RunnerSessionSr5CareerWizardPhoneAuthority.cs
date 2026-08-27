using System.Security.Cryptography;
using System.Text.Json;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

internal sealed record Sr5CareerWizardPhoneLoadResult(
    Sr5CareerWizardSnapshot? Snapshot,
    string? Blocker)
{
    public bool IsReady => Snapshot is not null && Blocker is null;
}

/// <summary>
/// Collects read-only typed Career presenter projections under a double-read workspace proof.
/// It never quotes, reviews, confirms, applies, or repairs an action.
/// </summary>
internal sealed class RunnerSessionSr5CareerWizardPhoneAuthority
{
    private const int MaximumProjectionBytes = 8 * 1024 * 1024;
    private readonly RunnerSessionCoordinator _coordinator;

    public RunnerSessionSr5CareerWizardPhoneAuthority(RunnerSessionCoordinator coordinator)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
    }

    public async Task<Sr5CareerWizardPhoneLoadResult> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!IsCreatedSr5(_coordinator.State))
        {
            return new(null, Sr5CareerWizardPhoneBlockers.WorkspaceAuthorityUnavailable);
        }

        NativeWorkspaceAuthoritySnapshot? first =
            await _coordinator.CaptureSr5CareerWizardWorkspaceAuthorityAsync(cancellationToken)
                .ConfigureAwait(false);
        if (first is null || !first.Matches(_coordinator.State))
        {
            return new(null, Sr5CareerWizardPhoneBlockers.WorkspaceAuthorityUnavailable);
        }

        try
        {
            List<Sr5CareerWizardPhoneAuthorityProbe> probes = [];
            foreach (Sr5CareerWizardPhoneActionDefinition action in Sr5CareerWizardPhoneCatalog.Actions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                RequireCurrent(first);
                Sr5CareerWizardPhoneAuthorityProbe? probe =
                    await LoadProbeAsync(action.ActionId, first, cancellationToken)
                        .ConfigureAwait(false);
                if (probe is not null)
                    probes.Add(probe);
            }

            NativeWorkspaceAuthoritySnapshot? verified =
                await _coordinator.CaptureSr5CareerWizardWorkspaceAuthorityAsync(cancellationToken)
                    .ConfigureAwait(false);
            if (verified is null || verified != first || !verified.Matches(_coordinator.State))
            {
                return new(null, Sr5CareerWizardPhoneBlockers.WorkspaceChangedDuringProjection);
            }

            var workspace = new Sr5CareerWizardPhoneWorkspaceAuthority(
                first.WorkspaceId,
                first.ContentRevision,
                first.SavedRevision,
                "sr5",
                first.PayloadSha256,
                first.DocumentSha256);
            return new(Sr5CareerWizardPhoneProjection.Project(workspace, probes), null);
        }
        catch (Sr5CareerWizardPhoneStaleProjectionException)
        {
            return new(null, Sr5CareerWizardPhoneBlockers.WorkspaceChangedDuringProjection);
        }
    }

    private Task<Sr5CareerWizardPhoneAuthorityProbe?> LoadProbeAsync(
        string actionId,
        NativeWorkspaceAuthoritySnapshot expected,
        CancellationToken cancellationToken)
        => actionId switch
        {
            Sr5CareerWizardActionIds.AdjustKarma => LoadTypedAsync(
                actionId,
                _coordinator.PrepareCareerManualKarmaEditAsync,
                static state => state.WorkspaceId.Value,
                static state => state.ContentRevision,
                static _ => true,
                expected,
                cancellationToken),
            Sr5CareerWizardActionIds.AdjustNuyen => LoadTypedAsync(
                actionId,
                _coordinator.PrepareCareerManualNuyenEditAsync,
                static state => state.WorkspaceId.Value,
                static state => state.ContentRevision,
                static _ => true,
                expected,
                cancellationToken),
            Sr5CareerWizardActionIds.EditKarmaExpense => LoadTypedAsync(
                actionId,
                _coordinator.PrepareCareerKarmaExpenseEditAsync,
                static state => state.WorkspaceId.Value,
                static state => state.ContentRevision,
                static state => state.Expenses.Count > 0,
                expected,
                cancellationToken),
            Sr5CareerWizardActionIds.EditNuyenExpense => LoadTypedAsync(
                actionId,
                _coordinator.PrepareCareerNuyenExpenseEditAsync,
                static state => state.WorkspaceId.Value,
                static state => state.ContentRevision,
                static state => state.Expenses.Count > 0,
                expected,
                cancellationToken),
            Sr5CareerWizardActionIds.AdvanceAttribute => LoadTypedAsync(
                actionId,
                _coordinator.PrepareCareerAttributeAdvanceAsync,
                static state => state.WorkspaceId.Value,
                static state => state.ContentRevision,
                static state => state.Attributes.Count > 0,
                expected,
                cancellationToken),
            Sr5CareerWizardActionIds.AdvanceActiveSkill => LoadTypedAsync(
                actionId,
                _coordinator.PrepareCareerActiveSkillAdvanceAsync,
                static state => state.WorkspaceId.Value,
                static state => state.ContentRevision,
                static state => state.Skills.Count > 0,
                expected,
                cancellationToken),
            Sr5CareerWizardActionIds.AdvanceKnowledgeSkill => LoadTypedAsync(
                actionId,
                _coordinator.PrepareCareerKnowledgeSkillAdvanceAsync,
                static state => state.WorkspaceId.Value,
                static state => state.ContentRevision,
                static state => state.Skills.Count > 0,
                expected,
                cancellationToken),
            Sr5CareerWizardActionIds.AdvanceSkillGroup => LoadTypedAsync(
                actionId,
                _coordinator.PrepareCareerSkillGroupAdvanceAsync,
                static state => state.WorkspaceId.Value,
                static state => state.ContentRevision,
                static state => state.SkillGroups.Count > 0,
                expected,
                cancellationToken),
            Sr5CareerWizardActionIds.LearnSpecialization => LoadTypedAsync(
                actionId,
                _coordinator.PrepareCareerSkillSpecializationAsync,
                static state => state.WorkspaceId.Value,
                static state => state.ContentRevision,
                static state => state.Skills.Count > 0,
                expected,
                cancellationToken),
            Sr5CareerWizardActionIds.ChangeQuality => LoadQualityAsync(
                actionId,
                expected,
                cancellationToken),
            Sr5CareerWizardActionIds.BeforeRun => LoadTypedAsync(
                actionId,
                token => new RunnerSessionSr5TableWizardPhoneAuthority(_coordinator)
                    .LoadAsync(Sr5TableWizardLane.BeforeRun, token),
                static state => state.WorkspaceId.Value,
                static state => state.WorkspaceRevision,
                static state => state.Actions.Count > 0,
                expected,
                cancellationToken),
            Sr5CareerWizardActionIds.Playtime => LoadTypedAsync(
                actionId,
                token => new RunnerSessionSr5TableWizardPhoneAuthority(_coordinator)
                    .LoadAsync(Sr5TableWizardLane.Playtime, token),
                static state => state.WorkspaceId.Value,
                static state => state.WorkspaceRevision,
                static state => state.Actions.Count > 0,
                expected,
                cancellationToken),
            Sr5CareerWizardActionIds.ManageCalendarEntry => LoadTypedAsync(
                actionId,
                _coordinator.PrepareCareerCalendarEditAsync,
                static state => state.WorkspaceId.Value,
                static state => state.ContentRevision,
                static _ => true,
                expected,
                cancellationToken),
            _ => throw new InvalidOperationException("Unknown SR5 Career action probe.")
        };

    private async Task<Sr5CareerWizardPhoneAuthorityProbe?> LoadQualityAsync(
        string actionId,
        NativeWorkspaceAuthoritySnapshot expected,
        CancellationToken cancellationToken)
    {
        CareerQualityEditorState? state;
        try
        {
            state = await _coordinator.PrepareCareerQualityAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
        if (state is null)
            return null;
        if (!string.Equals(state.RulesetId, "sr5", StringComparison.Ordinal)
            || state.SavedRevision != expected.SavedRevision)
        {
            throw new Sr5CareerWizardPhoneStaleProjectionException();
        }
        return CreateProbe(
            actionId,
            state,
            state.WorkspaceId.Value,
            state.WorkspaceRevision,
            state.Quotes.Count > 0,
            expected);
    }

    private async Task<Sr5CareerWizardPhoneAuthorityProbe?> LoadTypedAsync<T>(
        string actionId,
        Func<CancellationToken, Task<T?>> loader,
        Func<T, string> workspaceId,
        Func<T, long> workspaceRevision,
        Func<T, bool> hasEligibleTarget,
        NativeWorkspaceAuthoritySnapshot expected,
        CancellationToken cancellationToken)
        where T : class
    {
        T? state;
        try
        {
            state = await loader(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
        if (state is null)
            return null;
        return CreateProbe(
            actionId,
            state,
            workspaceId(state),
            workspaceRevision(state),
            hasEligibleTarget(state),
            expected);
    }

    private static Sr5CareerWizardPhoneAuthorityProbe CreateProbe<T>(
        string actionId,
        T state,
        string workspaceId,
        long workspaceRevision,
        bool available,
        NativeWorkspaceAuthoritySnapshot expected)
    {
        if (!string.Equals(workspaceId, expected.WorkspaceId, StringComparison.Ordinal)
            || workspaceRevision != expected.ContentRevision)
        {
            throw new Sr5CareerWizardPhoneStaleProjectionException();
        }

        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(state);
        try
        {
            if (payload.Length is 0 or > MaximumProjectionBytes)
                throw new InvalidOperationException("A typed Career projection exceeded its bounded digest input.");
            return new Sr5CareerWizardPhoneAuthorityProbe(
                actionId,
                available,
                available ? [] : [Sr5CareerWizardPhoneBlockers.NoEligibleTarget],
                SourceAnchorIds: [],
                Sr5CareerWizardPhoneProjection.DigestProjection(payload));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    private void RequireCurrent(NativeWorkspaceAuthoritySnapshot expected)
    {
        if (!expected.Matches(_coordinator.State) || !IsCreatedSr5(_coordinator.State))
            throw new Sr5CareerWizardPhoneStaleProjectionException();
    }

    private static bool IsCreatedSr5(CharacterOverviewState state)
        => state.Profile?.Created == true
           && Sr5CareerWizardCatalog.IsSr5CareerRunner(
               characterCreated: true,
               state.Rules?.GameEdition);

    private sealed class Sr5CareerWizardPhoneStaleProjectionException : Exception
    {
    }
}

internal sealed class PreferencesSr5CareerWizardPhoneCheckpointBackend :
    ISr5CareerWizardPhoneCheckpointBackend
{
    internal const string StorageKey = "chummer.android.sr5-career-wizard.navigation.v1";

    public string Read() => Preferences.Default.Get(StorageKey, string.Empty);
    public void Write(string payload) => Preferences.Default.Set(StorageKey, payload);
    public void Remove() => Preferences.Default.Remove(StorageKey);
}
