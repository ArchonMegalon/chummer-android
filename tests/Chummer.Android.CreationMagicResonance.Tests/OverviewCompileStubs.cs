using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Presentation.Overview;

public sealed record CharacterOverviewProfileStub(bool Created);

public sealed class CharacterOverviewState
{
    public CharacterOverviewProfileStub? Profile { get; init; }
    public CharacterWorkspaceId? WorkspaceId { get; init; }
    public long ContentRevision { get; init; }
    public long SavedRevision { get; init; }
    public bool IsDirty { get; init; }
    public string? Error { get; init; }
    public CharacterCreationMagicResonanceState? CreationMagicResonance { get; init; }
    public CharacterCreationMagicResonanceEditorState? CreationMagicResonanceEditor { get; init; }
}
