namespace Chummer.Contracts.Workspaces
{
    public readonly record struct CharacterWorkspaceId(string Value);
    public sealed record WorkspaceDocumentState(string Payload);
    public sealed record WorkspaceDocument(WorkspaceDocumentState State)
    {
        public WorkspaceDocument(string content) : this(new WorkspaceDocumentState(content)) { }
        public string Content => State.Payload;
    }
}

namespace Chummer.Application.Characters
{
    using Chummer.Contracts.Characters;

    public interface ICharacterSourceDataResolver
    {
        ICharacterSourceDataContext? TryCreateContext(string characterXml);
    }

    public interface ICharacterSourceDataContext
    {
        bool TryResolveVehicleWorkshopCatalog(out CharacterVehicleWorkshopCatalog catalog);
    }
}
