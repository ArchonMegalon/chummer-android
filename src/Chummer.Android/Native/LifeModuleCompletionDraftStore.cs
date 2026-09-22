using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Android.Native;

/// <summary>App-private input drafts only. No saved preview or permission to mutate a runner.</summary>
public sealed class LifeModuleCompletionDraftStore(string stateDirectory)
{
    private const int MaximumBytes = 2 * 1024 * 1024;
    private readonly string _directory = Path.Combine(stateDirectory, "life-module-completion-inputs");
    private readonly SemaphoreSlim _gate = new(1, 1);
    private sealed record Draft(string Schema, string Owner, CharacterCreationFoundationFinalizationPreviewRequest Input);
    private const string Schema = "chummer.android.life-module-completion-inputs/v1";

    internal async Task<CharacterCreationFoundationFinalizationPreviewRequest?> LoadAsync(string owner, CharacterWorkspaceId id, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            string path = PathFor(owner, id);
            if (!File.Exists(path)) return null;
            await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
            if (input.Length > MaximumBytes) throw new InvalidDataException("Life Modules input draft is oversized.");
            byte[] bytes = new byte[MaximumBytes + 1];
            int used = 0, read;
            while (used < bytes.Length && (read = await input.ReadAsync(bytes.AsMemory(used), ct).ConfigureAwait(false)) != 0) used += read;
            if (used > MaximumBytes) throw new InvalidDataException("Life Modules input draft is oversized.");
            var saved = JsonSerializer.Deserialize<Draft>(bytes.AsSpan(0, used));
            if (saved is null || saved.Schema != Schema || saved.Owner != owner || saved.Input?.Binding is null || saved.Input.Binding.WorkspaceId != id)
                throw new InvalidDataException("Life Modules input draft has a different identity.");
            return saved.Input;
        }
        finally { _gate.Release(); }
    }

    internal async Task SaveAsync(string owner, CharacterCreationFoundationFinalizationPreviewRequest input, CancellationToken ct)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new Draft(Schema, owner, input));
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("Life Modules input draft is oversized.");
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        string? temporary = null;
        try
        {
            Directory.CreateDirectory(_directory);
            string path = PathFor(owner, input.Binding.WorkspaceId);
            temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true))
            {
                await output.WriteAsync(bytes, ct).ConfigureAwait(false);
                await output.FlushAsync(ct).ConfigureAwait(false);
                output.Flush(flushToDisk: true);
            }
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
            temporary = null;
        }
        finally
        {
            if (temporary is not null) try { File.Delete(temporary); } catch (IOException) { }
            _gate.Release();
        }
    }

    private string PathFor(string owner, CharacterWorkspaceId id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        return Path.Combine(_directory, Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(owner + "\0" + id.Value))) + ".json");
    }
}
