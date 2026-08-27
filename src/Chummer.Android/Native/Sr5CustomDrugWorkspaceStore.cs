using Chummer.Contracts.Workspaces;

namespace Chummer.Android.Native;

public sealed record Sr5CustomDrugWorkspaceSnapshot(
    CharacterWorkspaceId WorkspaceId,
    long ContentRevision,
    long SavedRevision,
    WorkspaceDocument Document);

public sealed record Sr5CustomDrugWorkspaceWriteResult(
    bool Applied,
    bool Conflict,
    long ContentRevision,
    long SavedRevision,
    string Error);

public interface ISr5CustomDrugWorkspaceStore
{
    Sr5CustomDrugWorkspaceSnapshot? Read(CharacterWorkspaceId workspaceId);

    Sr5CustomDrugWorkspaceWriteResult ReplaceAndCheckpoint(
        Sr5CustomDrugWorkspaceSnapshot expected,
        string characterXml);
}
