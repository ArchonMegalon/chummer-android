using Chummer.Contracts.Workspaces;

namespace Chummer.Android.Native;

public sealed record Sr5CareerCyberwareWorkspaceSnapshot(
    CharacterWorkspaceId WorkspaceId,
    long ContentRevision,
    long SavedRevision,
    WorkspaceDocument Document);

public sealed record Sr5CareerCyberwareWorkspaceWriteResult(
    bool Applied,
    bool Conflict,
    long ContentRevision,
    long SavedRevision,
    string Error);

public interface ISr5CareerCyberwareWorkspaceStore
{
    Sr5CareerCyberwareWorkspaceSnapshot? Read(CharacterWorkspaceId workspaceId);

    Sr5CareerCyberwareWorkspaceWriteResult ReplaceAndCheckpoint(
        Sr5CareerCyberwareWorkspaceSnapshot expected,
        string characterXml);
}
