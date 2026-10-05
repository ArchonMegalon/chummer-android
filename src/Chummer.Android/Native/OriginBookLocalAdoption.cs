using System.Security.Cryptography;
using System.Text.Json;
using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Android.Native;

// These are storage helpers, never account admission. The adoption coordinator
// calls them on a worker under the actual account lease, after Core's durable
// claim. Original local files remain private recovery copies, not another runner.
public sealed partial class OriginBookReadingStore
{
    internal void RequireAdoptionDestinationAbsent(string owner, string workspace)
        => OriginAdoptionFiles.RequireAbsent(PathFor(owner, workspace));
    internal void AdoptLocalEdition(string owner, string workspace, string expectedLocalDigest,
        Func<bool> stillCurrent, CancellationToken ct)
    {
        OriginAdoptionFiles.RequireAccount(owner);
        lock (_gate)
        {
            ct.ThrowIfCancellationRequested();
            if (!stillCurrent()) throw new OperationCanceledException("The account changed.");
            Directory.CreateDirectory(_root);
            using var sourceLock = OriginAdoptionFiles.Lock(PathFor(OwnerScope.LocalSingleUser.NormalizedValue, workspace));
            var local = Load(OwnerScope.LocalSingleUser.NormalizedValue, workspace);
            if (local.Digest != expectedLocalDigest) throw new InvalidOperationException("The local reading edition changed.");
            var next = local with { Owner = owner };
            var current = Load(owner, workspace);
            if (current.Digest == next.Digest) return;
            if (File.Exists(PathFor(owner, workspace))) throw new InvalidOperationException("An account reading edition already exists.");
            Save(current, next, stillCurrent, ct);
        }
    }
}

public sealed partial class OriginBookSceneStore
{
    internal void RequireAdoptionDestinationAbsent(string owner, string workspace)
        => OriginAdoptionFiles.RequireAbsent(PathFor(owner, workspace));
    internal void AdoptLocalScenes(string owner, string workspace, string expectedLocalDigest,
        Func<bool> stillCurrent, CancellationToken ct)
    {
        OriginAdoptionFiles.RequireAccount(owner);
        lock (_gate)
        {
            ct.ThrowIfCancellationRequested();
            if (!stillCurrent()) throw new OperationCanceledException("The account changed.");
            Directory.CreateDirectory(_root);
            using var sourceLock = OriginAdoptionFiles.Lock(PathFor(OwnerScope.LocalSingleUser.NormalizedValue, workspace));
            var local = Load(OwnerScope.LocalSingleUser.NormalizedValue, workspace);
            if (local.Digest != expectedLocalDigest) throw new InvalidOperationException("The local book illustrations changed.");
            var next = new OriginBookScenes(owner, workspace, local.Scenes);
            var current = Load(owner, workspace);
            if (current.Digest == next.Digest) return;
            if (File.Exists(PathFor(owner, workspace))) throw new InvalidOperationException("Account book illustrations already exist.");
            Save(current, next, stillCurrent, ct);
        }
    }
}

public sealed partial class FileOriginDossierDraftTimelineStore
{
    internal void RequireAdoptionDestinationAbsent(string owner, string workspace)
        => OriginAdoptionFiles.RequireAbsent(ResolvePath(owner, workspace));
    internal LifeModuleOriginDossierDraftCheckpoint? ReadForAdoption(string owner, string workspace)
    {
        ValidateIdentity(owner, workspace);
        _gate.Wait();
        try
        {
            var bytes = OriginAdoptionFiles.Read(ResolvePath(owner, workspace), 16 * 1024 * 1024);
            if (bytes is null) return null;
            var checkpoint = JsonSerializer.Deserialize<LifeModuleOriginDossierDraftCheckpoint>(bytes, JsonOptions);
            if (checkpoint?.Schema != OriginDossierSchemas.DraftCheckpointV1 || checkpoint.OwnerId != owner
                || checkpoint.WorkspaceId != workspace || string.IsNullOrWhiteSpace(checkpoint.CheckpointDigest))
                throw new InvalidDataException("The Origin timeline has an invalid identity.");
            return checkpoint;
        }
        finally { _gate.Release(); }
    }

    internal void RetainAdoptedCheckpoint(LifeModuleOriginDossierDraftCheckpoint checkpoint,
        Func<bool> stillCurrent, CancellationToken ct)
    {
        OriginAdoptionFiles.RequireAccount(checkpoint.OwnerId);
        ValidateIdentity(checkpoint.OwnerId, checkpoint.WorkspaceId);
        _gate.Wait(ct);
        try
        {
            Directory.CreateDirectory(_directory);
            OriginAdoptionFiles.CommitNewOrExact(ResolvePath(checkpoint.OwnerId, checkpoint.WorkspaceId),
                JsonSerializer.SerializeToUtf8Bytes(checkpoint, JsonOptions), stillCurrent, ct);
        }
        finally { _gate.Release(); }
    }
}

public sealed partial class LifeModuleCompletionDraftStore
{
    internal void RequireAdoptionDestinationAbsent(string owner, CharacterWorkspaceId workspace)
        => OriginAdoptionFiles.RequireAbsent(PathFor(owner, workspace));
    internal CharacterCreationFoundationFinalizationPreviewRequest? ReadForAdoption(string owner, CharacterWorkspaceId id)
    {
        _gate.Wait();
        try
        {
            var bytes = OriginAdoptionFiles.Read(PathFor(owner, id), MaximumBytes);
            if (bytes is null) return null;
            var saved = JsonSerializer.Deserialize<Draft>(bytes);
            if (saved?.Schema != Schema || saved.Owner != owner || saved.Input?.Binding is null || saved.Input.Binding.WorkspaceId != id)
                throw new InvalidDataException("The Life Modules input draft has an invalid identity.");
            return saved.Input;
        }
        finally { _gate.Release(); }
    }

    internal void RetainAdoptedInputs(string owner, CharacterCreationFoundationFinalizationPreviewRequest input,
        Func<bool> stillCurrent, CancellationToken ct)
    {
        OriginAdoptionFiles.RequireAccount(owner);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new Draft(Schema, owner, input));
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("The Life Modules input draft is oversized.");
        _gate.Wait(ct);
        try
        {
            Directory.CreateDirectory(_directory);
            OriginAdoptionFiles.CommitNewOrExact(PathFor(owner, input.Binding.WorkspaceId), bytes, stillCurrent, ct);
        }
        finally { _gate.Release(); }
    }
}

internal static class OriginAdoptionFiles
{
    internal static void RequireAbsent(string path)
    {
        RejectLink(path);
        if (File.Exists(path)) throw new InvalidOperationException("Account book data already exists; nothing was overwritten.");
    }
    internal static void RequireAccount(string owner)
    {
        if (string.IsNullOrWhiteSpace(owner) || owner != new OwnerScope(owner).NormalizedValue
            || new OwnerScope(owner).UsesLocalSingleUserValue)
            throw new InvalidDataException("An exact linked account is required.");
    }

    internal static FileStream Lock(string path)
    {
        RejectLink(path);
        RejectLink(path + ".lock");
        return new(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    internal static byte[]? Read(string path, int maximum)
    {
        RejectLink(path);
        if (!File.Exists(path)) return null;
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length < 1 || input.Length > maximum) throw new InvalidDataException("The private adoption input is oversized or empty.");
        byte[] bytes = new byte[checked((int)input.Length)];
        input.ReadExactly(bytes);
        if (input.ReadByte() != -1) throw new InvalidDataException("The private adoption input changed size.");
        return bytes;
    }

    internal static string Digest<T>(T value) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));

    internal static void CommitNewOrExact(string path, byte[] bytes, Func<bool> stillCurrent, CancellationToken ct)
    {
        using var custody = Lock(path);
        ct.ThrowIfCancellationRequested();
        if (!stillCurrent()) throw new OperationCanceledException("The account changed.");
        if (File.Exists(path))
        {
            if (Read(path, bytes.Length)!.AsSpan().SequenceEqual(bytes)) return;
            throw new InvalidOperationException("Different account data already exists; nothing was overwritten.");
        }
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                output.Write(bytes);
                output.Flush(flushToDisk: true);
            }
            ct.ThrowIfCancellationRequested();
            if (!stillCurrent()) throw new OperationCanceledException("The account changed.");
            File.Move(temporary, path, overwrite: false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void RejectLink(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked private adoption paths are not accepted.");
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
    }
}
