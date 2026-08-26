namespace Chummer.Contracts.Workspaces
{
    public readonly record struct CharacterWorkspaceId(string Value);

    public enum WorkspaceOperationOutcome
    {
        Success,
        Missing,
        Conflict,
        Corrupt,
        Unavailable
    }

    public enum WorkspaceDocumentFormat
    {
        NativeXml,
        Json
    }

    public sealed record WorkspaceDocumentState(
        string RulesetId,
        string Payload);

    public sealed record WorkspaceDocument(
        WorkspaceDocumentState State,
        WorkspaceDocumentFormat Format = WorkspaceDocumentFormat.NativeXml)
    {
        public WorkspaceDocument(
            string content,
            string rulesetId,
            WorkspaceDocumentFormat format = WorkspaceDocumentFormat.NativeXml)
            : this(new WorkspaceDocumentState(rulesetId, content), format)
        {
        }

        public string Content => State.Payload;
        public string RulesetId => State.RulesetId;
    }
}

namespace Chummer.Application.Workspaces
{
    using Chummer.Contracts.Workspaces;

    public readonly record struct WorkspaceStoreEntry(
        CharacterWorkspaceId Id,
        DateTimeOffset LastUpdatedUtc,
        long ContentRevision,
        long SavedRevision);

    public sealed record WorkspaceStoredDocument(
        CharacterWorkspaceId Id,
        WorkspaceDocument Document,
        long ContentRevision,
        long SavedRevision,
        DateTimeOffset LastUpdatedUtc);

    public sealed record WorkspaceStoreReadResult(
        WorkspaceOperationOutcome Outcome,
        WorkspaceStoredDocument? Value = null,
        string? Error = null)
    {
        public bool Success => Outcome == WorkspaceOperationOutcome.Success && Value is not null;
    }

    public sealed record WorkspaceStoreMutationResult(
        WorkspaceOperationOutcome Outcome,
        WorkspaceStoreEntry? Entry = null,
        string? Error = null)
    {
        public bool Success => Outcome == WorkspaceOperationOutcome.Success && Entry is not null;
    }

    public interface IWorkspaceStore
    {
        WorkspaceStoreReadResult Get(CharacterWorkspaceId id);

        WorkspaceStoreMutationResult ReplaceWorkspaceDocumentAndCheckpoint(
            CharacterWorkspaceId id,
            long expectedContentRevision,
            WorkspaceDocument document);
    }
}

namespace Chummer.Application.Characters
{
    public interface ICharacterSourceDataResolver
    {
        ICharacterSourceDataContext? TryCreateContext(string characterXml);
    }

    public interface ICharacterSourceDataContext
    {
        bool TryResolveActiveSkillSource(
            string sourceSkillId,
            out CharacterActiveSkillSource source);
    }

    public sealed record CharacterActiveSkillSource(
        string SourceSkillId,
        string Name,
        string SkillCategory,
        string SkillGroup,
        string DefaultAttribute,
        bool IsExotic,
        bool RequiresGroundMovement,
        bool RequiresSwimMovement,
        bool RequiresFlyMovement,
        string RawSourceXml)
    {
        public static CharacterActiveSkillSource Unavailable { get; } = new(
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            false,
            false,
            false,
            false,
            string.Empty);
    }
}
