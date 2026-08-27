namespace Chummer.Contracts.Workspaces;

public readonly record struct CharacterWorkspaceId(string Value)
{
    public override string ToString() => Value;
}

public sealed record WorkspaceDocumentState(string Payload);

public sealed record WorkspaceDocument(WorkspaceDocumentState State)
{
    public WorkspaceDocument(string content) : this(new WorkspaceDocumentState(content))
    {
    }

    public string Content => State.Payload;
}
