using Chummer.Application.Workspaces;
using Chummer.Contracts.Workspaces;

namespace Chummer.Android.Native;

/// <summary>
/// Narrow atomic persistence adapter. Only authority-returned Career XML can be
/// passed into this seam; no creation page receives this capability.
/// </summary>
public sealed class AndroidSr5CustomDrugWorkspaceStore(IWorkspaceStore store)
    : ISr5CustomDrugWorkspaceStore
{
    public Sr5CustomDrugWorkspaceSnapshot? Read(CharacterWorkspaceId workspaceId)
    {
        WorkspaceStoreReadResult read = store.Get(workspaceId);
        return read.Success && read.Value is { } value
            ? new Sr5CustomDrugWorkspaceSnapshot(
                workspaceId,
                value.ContentRevision,
                value.SavedRevision,
                value.Document)
            : null;
    }

    public Sr5CustomDrugWorkspaceWriteResult ReplaceAndCheckpoint(
        Sr5CustomDrugWorkspaceSnapshot expected,
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
            ? new Sr5CustomDrugWorkspaceWriteResult(
                result.Success,
                result.Outcome == WorkspaceOperationOutcome.Conflict,
                entry.ContentRevision,
                entry.SavedRevision,
                result.Error ?? string.Empty)
            : new Sr5CustomDrugWorkspaceWriteResult(
                Applied: false,
                result.Outcome == WorkspaceOperationOutcome.Conflict,
                expected.ContentRevision,
                expected.SavedRevision,
                result.Error ?? string.Empty);
    }
}
