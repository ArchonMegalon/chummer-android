using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Android.Native;

/// <summary>
/// Read-only Android adapter that gives the manual run-result host the same
/// clean saved character facts later consumed by Core's settlement workspace.
/// It never writes or rewrites generic XML.
/// </summary>
internal sealed class AndroidAfterRunWorkspaceSnapshotSource(IWorkspaceStore store) :
    IAndroidAfterRunWorkspaceSnapshotSource
{
    private const long MaximumCharacterXmlLength = 67_108_864;
    private readonly IWorkspaceStore _store = store
        ?? throw new ArgumentNullException(nameof(store));

    public bool TryRead(
        CharacterWorkspaceId workspaceId,
        out Sr5AfterRunWorkspaceSnapshot snapshot,
        out string blocker)
    {
        snapshot = null!;
        blocker = string.Empty;
        if (!CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(workspaceId))
        {
            blocker = "The saved runner workspace identity is invalid.";
            return false;
        }

        WorkspaceStoreReadResult read;
        try
        {
            read = _store.Get(workspaceId);
        }
        catch (Exception error) when (error is IOException
            or UnauthorizedAccessException
            or InvalidOperationException)
        {
            blocker = "The saved runner workspace is unavailable.";
            return false;
        }
        if (!read.Success
            || read.Value is not { } saved
            || saved.Id != workspaceId
            || saved.ContentRevision <= 0
            || saved.SavedRevision != saved.ContentRevision
            || saved.Document.Format != WorkspaceDocumentFormat.NativeXml
            || !string.Equals(
                saved.Document.RulesetId,
                CharacterAfterRunSettlementRules.RulesetId,
                StringComparison.Ordinal))
        {
            blocker = "After Run entry requires an exact clean saved SR5 runner.";
            return false;
        }

        try
        {
            XDocument document = Parse(saved.Document.Content);
            XElement root = document.Root
                ?? throw new InvalidOperationException("missing_character_root");
            if (root.Name != XName.Get("character"))
            {
                throw new InvalidOperationException("invalid_character_root");
            }
            snapshot = new Sr5AfterRunWorkspaceSnapshot(
                workspaceId,
                saved.ContentRevision,
                saved.SavedRevision,
                saved.Document.RulesetId,
                ReadBool(root, "created"),
                ReadNonNegativeInt(root, "streetcred"),
                ReadNonNegativeInt(root, "notoriety"),
                ReadNonNegativeInt(root, "publicawareness"),
                ReadNonNegativeInt(root, "karma"),
                Convert.ToHexStringLower(SHA256.HashData(
                    Encoding.UTF8.GetBytes(saved.Document.Content))));
            if (!snapshot.IsExact())
            {
                snapshot = null!;
                blocker = "The saved runner projection is incomplete or incoherent.";
                return false;
            }
            return true;
        }
        catch (Exception error) when (error is XmlException
            or InvalidOperationException
            or FormatException
            or OverflowException)
        {
            snapshot = null!;
            blocker = "The saved SR5 runner cannot be projected exactly for After Run entry.";
            return false;
        }
    }

    private static XDocument Parse(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml) || xml.Length > MaximumCharacterXmlLength)
        {
            throw new InvalidOperationException("character_xml_size_invalid");
        }
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumCharacterXmlLength
        };
        using var text = new StringReader(xml);
        using XmlReader reader = XmlReader.Create(text, settings);
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
    }

    private static bool ReadBool(XElement root, string name)
        => bool.TryParse(RequireSingle(root, name).Value.Trim(), out bool value)
            ? value
            : throw new InvalidOperationException($"invalid_{name}");

    private static int ReadNonNegativeInt(XElement root, string name)
        => int.TryParse(
                RequireSingle(root, name).Value.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int value)
            && value is >= 0 and <= CharacterAfterRunSettlementRules.MaximumValue
                ? value
                : throw new InvalidOperationException($"invalid_{name}");

    private static XElement RequireSingle(XElement root, string name)
    {
        XElement[] values = root.Elements(name).Take(2).ToArray();
        return values.Length == 1
            ? values[0]
            : throw new InvalidOperationException($"missing_or_duplicate_{name}");
    }
}
