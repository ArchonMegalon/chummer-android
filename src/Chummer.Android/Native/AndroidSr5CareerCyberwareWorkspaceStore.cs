using Chummer.Application.Workspaces;
using Chummer.Contracts.Workspaces;

namespace Chummer.Android.Native;

/// <summary>
/// Narrow CAS adapter. Only XML returned by the typed Core purchase authority
/// reaches this boundary.
/// </summary>
public sealed class AndroidSr5CareerCyberwareWorkspaceStore(IWorkspaceStore store)
    : ISr5CareerCyberwareWorkspaceStore
{
    public Sr5CareerCyberwareWorkspaceSnapshot? Read(CharacterWorkspaceId workspaceId)
    {
        WorkspaceStoreReadResult read = store.Get(workspaceId);
        return read.Success && read.Value is { } value
            ? new Sr5CareerCyberwareWorkspaceSnapshot(
                workspaceId,
                value.ContentRevision,
                value.SavedRevision,
                value.Document)
            : null;
    }

    public Sr5CareerCyberwareWorkspaceWriteResult ReplaceAndCheckpoint(
        Sr5CareerCyberwareWorkspaceSnapshot expected,
        string characterXml)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentException.ThrowIfNullOrWhiteSpace(characterXml);
        WorkspaceDocument replacement = expected.Document with
        {
            State = expected.Document.State with { Payload = characterXml }
        };
        WorkspaceStoreMutationResult result = store.ReplaceWorkspaceDocumentAndCheckpoint(
            expected.WorkspaceId,
            expected.ContentRevision,
            replacement);
        return result.Entry is { } entry
            ? new Sr5CareerCyberwareWorkspaceWriteResult(
                result.Success,
                result.Outcome == WorkspaceOperationOutcome.Conflict,
                entry.ContentRevision,
                entry.SavedRevision,
                result.Error ?? string.Empty)
            : new Sr5CareerCyberwareWorkspaceWriteResult(
                Applied: false,
                result.Outcome == WorkspaceOperationOutcome.Conflict,
                expected.ContentRevision,
                expected.SavedRevision,
                result.Error ?? string.Empty);
    }
}
