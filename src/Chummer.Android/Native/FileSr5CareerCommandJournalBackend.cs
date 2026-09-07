using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Chummer.Android.Native;

public interface ISr5CareerCommandJournalBackend
{
    string Read();
    void Write(string payload);
}

public enum Sr5CareerCommandJournalDomain { AfterRunReward, Reputation }

/// <summary>Shared file durability only; each domain owns its typed journal validation.</summary>
public sealed class FileSr5CareerCommandJournalBackend : ISr5CareerCommandJournalBackend
{
    public const int MaximumBytes = 16 * 1024 * 1024;
    private readonly string _directory;
    private readonly string _name;
    private readonly string _path;

    public FileSr5CareerCommandJournalBackend(string stateDirectory, Sr5CareerCommandJournalDomain domain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateDirectory);
        _directory = Path.GetFullPath(stateDirectory);
        if (!Directory.Exists(_directory)) throw new DirectoryNotFoundException("The Career journal directory must already exist.");
        _name = domain switch
        {
            Sr5CareerCommandJournalDomain.AfterRunReward => "sr5-after-run-rewards",
            Sr5CareerCommandJournalDomain.Reputation => "sr5-career-reputation",
            _ => throw new ArgumentOutOfRangeException(nameof(domain))
        };
        _path = Path.Combine(_directory, _name + ".v1.json");
    }

    public string Read()
    {
        FileStream opened;
        try { opened = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read); }
        catch (FileNotFoundException) { return string.Empty; }
        using var stream = opened;
        if (stream.Length == 0 || stream.Length > MaximumBytes)
            throw new InvalidDataException("Existing Career journal is empty or oversized; preserve it.");
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true));
        string payload = reader.ReadToEnd();
        return payload.Length > 0 ? payload : throw new InvalidDataException("An empty Career journal is corrupt, not absent.");
    }

    public void Write(string payload)
    {
        if (!OperatingSystem.IsAndroid() && !OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("Durable Career journal directory sync requires Android/Linux.");
        if (Encoding.UTF8.GetByteCount(payload) > MaximumBytes)
            throw new InvalidDataException("Career journal capacity exceeded; history must be retained.");
        string temporary = Path.Combine(_directory, $".{_name}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
                writer.Write(payload);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, _path, overwrite: true);
            SyncDirectory(_directory);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void SyncDirectory(string directory)
    {
        int descriptor = Open(directory, 0x10000 | 0x80000);
        if (descriptor < 0) throw new IOException("Cannot open Career journal directory for sync.", new Win32Exception(Marshal.GetLastPInvokeError()));
        try
        {
            if (Fsync(descriptor) != 0) throw new IOException("Career journal directory sync acknowledgement is unavailable.",
                new Win32Exception(Marshal.GetLastPInvokeError()));
        }
        finally { Close(descriptor); }
    }
    [DllImport("libc", EntryPoint = "open", SetLastError = true)] private static extern int Open(string path, int flags);
    [DllImport("libc", EntryPoint = "fsync", SetLastError = true)] private static extern int Fsync(int descriptor);
    [DllImport("libc", EntryPoint = "close", SetLastError = true)] private static extern int Close(int descriptor);
}
