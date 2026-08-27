using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

internal sealed record Sr5DowntimeCalendarPhoneLoadResult(
    Sr5CareerWizardBinding? Binding,
    CareerCalendarEditorState? Editor,
    string? Blocker)
{
    public bool IsReady => Binding is not null && Editor is not null && Blocker is null;
}

/// <summary>
/// Loads the typed Calendar projection between two identical saved-document authority reads.
/// This seam is read-only; it grants no mutation authority and exposes no compatibility fallback.
/// </summary>
internal sealed class RunnerSessionSr5DowntimeCalendarAuthority(
    RunnerSessionCoordinator coordinator)
{
    public async Task<Sr5DowntimeCalendarPhoneLoadResult> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (coordinator.State.Profile?.Created != true
            || !Sr5CareerWizardCatalog.IsSr5CareerRunner(
                characterCreated: true,
                coordinator.State.Rules?.GameEdition)
            || coordinator.State.WorkspaceId is null
            || coordinator.State.ContentRevision <= 0
            || coordinator.State.SavedRevision != coordinator.State.ContentRevision
            || coordinator.State.IsDirty
            || coordinator.State.Error is not null)
        {
            return Unavailable();
        }

        NativeWorkspaceAuthoritySnapshot? before =
            await coordinator.CaptureSr5CareerWizardWorkspaceAuthorityAsync(cancellationToken)
                .ConfigureAwait(false);
        if (before is null || !before.Matches(coordinator.State))
            return Unavailable();

        CareerCalendarEditorState? editor;
        try
        {
            editor = await coordinator.PrepareCareerCalendarEditAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return new(null, null, "The typed SR5 Downtime Calendar authority is unavailable.");
        }

        NativeWorkspaceAuthoritySnapshot? after =
            await coordinator.CaptureSr5CareerWizardWorkspaceAuthorityAsync(cancellationToken)
                .ConfigureAwait(false);
        if (editor is null
            || after is null
            || after != before
            || !after.Matches(coordinator.State)
            || !string.Equals(editor.WorkspaceId.Value, before.WorkspaceId, StringComparison.Ordinal)
            || editor.ContentRevision != before.ContentRevision)
        {
            return new(null, null, "The runner changed while the typed Downtime Calendar was projected.");
        }

        try
        {
            Sr5CareerWizardBinding binding = Sr5DowntimeCalendarPhoneProjection.CreateBinding(
                new Sr5DowntimeCalendarWorkspaceAuthority(
                    before.WorkspaceId,
                    before.ContentRevision,
                    before.SavedRevision,
                    before.PayloadSha256,
                    before.DocumentSha256),
                editor);
            _ = new Sr5DowntimeCalendarDesktopSession().Bind(binding, editor);
            return new(binding, editor, null);
        }
        catch (InvalidOperationException)
        {
            return new(null, null, "The typed Downtime Calendar projection did not match its exact runtime, source, and content authority.");
        }
    }

    private static Sr5DowntimeCalendarPhoneLoadResult Unavailable()
        => new(null, null, "Downtime requires one clean, saved SR5 Career runner revision.");
}
