using System.Runtime.InteropServices;
using System.Text;
using Chummer.Android.Native;
using Chummer.Android.Platform;

// Package-free host syscall regression over the actual production sources.
// A Linux ARM64 pass is not an Android device, power-cut, or Play receipt.
internal static class Program
{
    private static void Main(string[] args)
    {
        if (args.Length != 2 || args[0] != "--require-architecture")
            throw new ArgumentException("Expected --require-architecture arm64|arm|x64|x86.");
        Architecture expected = args[1] switch
        {
            "arm64" => Architecture.Arm64,
            "arm" => Architecture.Arm,
            "x64" => Architecture.X64,
            "x86" => Architecture.X86,
            _ => throw new ArgumentException("Unsupported requested architecture.")
        };
        Require(OperatingSystem.IsLinux() || OperatingSystem.IsAndroid(), "A Linux/Android process is required.");
        Require(RuntimeInformation.ProcessArchitecture == expected,
            "The actual process architecture does not match the requested test architecture.");
        Console.WriteLine($"Actual process architecture: {RuntimeInformation.ProcessArchitecture}; OS: {RuntimeInformation.OSDescription}; PID: {Environment.ProcessId}; runtime: {RuntimeInformation.FrameworkDescription}");
        DirectoryInfo root = Directory.CreateTempSubdirectory("chummer-directory-durability-");
        try
        {
            (string Name, Action Run)[] tests =
            [
                ("supported-and-unsupported-ABI-flags", CheckFlags),
                ("real-existing-directory-fsync", () => AndroidPrivateFileDurability.SyncDirectory(root.FullName)),
                ("real-file-and-missing-target-rejection", () => CheckRejectedTargets(root.FullName)),
                ("real-created-directory-and-renamed-file", () => CheckDirectoryAndRename(root.FullName)),
                ("real-reward-journal-cold-read", () => CheckJournal(root.FullName, Sr5CareerCommandJournalDomain.AfterRunReward)),
                ("real-reputation-journal-cold-read", () => CheckJournal(root.FullName, Sr5CareerCommandJournalDomain.Reputation))
            ];
            foreach ((string name, Action run) in tests)
            {
                run();
                Console.WriteLine("PASS " + name);
            }
            Console.WriteLine($"PASS {tests.Length} real-helper host tests; no Android device or power-cut proof.");
        }
        finally
        {
            // Only the unique directory created by this process is removed.
            root.Delete(recursive: true);
        }
    }

    private static void CheckFlags()
    {
        Require(AndroidPrivateFileDurability.DirectoryOpenFlags(Architecture.Arm) == 0x84000, "ARM flags differ.");
        Require(AndroidPrivateFileDurability.DirectoryOpenFlags(Architecture.Arm64) == 0x84000, "ARM64 flags differ.");
        Require(AndroidPrivateFileDurability.DirectoryOpenFlags(Architecture.X86) == 0x90000, "x86 flags differ.");
        Require(AndroidPrivateFileDurability.DirectoryOpenFlags(Architecture.X64) == 0x90000, "x64 flags differ.");
        foreach (Architecture value in Enum.GetValues<Architecture>()
            .Where(value => value is not (Architecture.Arm or Architecture.Arm64 or Architecture.X86 or Architecture.X64))
            .Append((Architecture)(-1)).Append((Architecture)int.MaxValue))
        {
            bool rejected = false;
            try { AndroidPrivateFileDurability.DirectoryOpenFlags(value); }
            catch (PlatformNotSupportedException) { rejected = true; }
            Require(rejected, "Unsupported ABI was admitted: " + value);
        }
    }

    private static void CheckRejectedTargets(string root)
    {
        string file = Path.Combine(root, "not-a-directory");
        string missing = Path.Combine(root, "missing-directory");
        File.WriteAllText(file, "unchanged");
        RequireIOException(() => AndroidPrivateFileDurability.SyncDirectory(file));
        RequireIOException(() => AndroidPrivateFileDurability.SyncDirectory(missing));
        Require(File.ReadAllText(file) == "unchanged" && !Path.Exists(missing), "Rejected sync mutated its targets.");
    }

    private static void CheckDirectoryAndRename(string root)
    {
        string directory = Path.Combine(root, "created");
        Directory.CreateDirectory(directory);
        AndroidPrivateFileDurability.SyncDirectory(root);
        string temporary = Path.Combine(directory, "temporary");
        string final = Path.Combine(directory, "published");
        byte[] bytes = Encoding.UTF8.GetBytes("exact-ä-Ω\n");
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, final, overwrite: false);
        AndroidPrivateFileDurability.SyncDirectory(directory);
        Require(File.ReadAllBytes(final).SequenceEqual(bytes) && !Path.Exists(temporary), "Published bytes differ.");
    }

    private static void CheckJournal(string root, Sr5CareerCommandJournalDomain domain)
    {
        string directory = Path.Combine(root, domain.ToString());
        Directory.CreateDirectory(directory);
        var backend = new FileSr5CareerCommandJournalBackend(directory, domain);
        Require(backend.Read() == string.Empty, "Fresh journal is not empty.");
        backend.Write("first-ä\n");
        Require(new FileSr5CareerCommandJournalBackend(directory, domain).Read() == "first-ä\n", "Initial cold read differs.");
        backend.Write("second-Ω\n");
        Require(new FileSr5CareerCommandJournalBackend(directory, domain).Read() == "second-Ω\n", "Replacement cold read differs.");
        string[] files = Directory.GetFileSystemEntries(directory);
        Require(files.Length == 1 && files[0].EndsWith(".v1.json", StringComparison.Ordinal), "Journal retained unexpected entries.");
    }

    private static void RequireIOException(Action action)
    {
        bool rejected = false;
        try { action(); }
        catch (IOException) { rejected = true; }
        Require(rejected, "Directory sync admitted a regular file or absent path.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
