using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Chummer.Android.Platform;

/// <summary>OS durability acknowledgement for app-private Android/Linux directory entries.</summary>
internal static class AndroidPrivateFileDurability
{
    internal static void SyncDirectory(string directory)
    {
        if (!OperatingSystem.IsAndroid() && !OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("Private file directory sync requires Android/Linux.");
        int descriptor = Open(directory, DirectoryOpenFlags(RuntimeInformation.ProcessArchitecture));
        if (descriptor < 0)
            throw new IOException("Cannot open private file directory for sync.",
                new Win32Exception(Marshal.GetLastPInvokeError()));
        try
        {
            if (Fsync(descriptor) != 0)
                throw new IOException("Private file directory sync acknowledgement is unavailable.",
                    new Win32Exception(Marshal.GetLastPInvokeError()));
        }
        finally { Close(descriptor); }
    }

    // Linux UAPI values are architecture-specific, including Android bionic.
    // arch/{arm,arm64}/include/uapi/asm/fcntl.h overrides asm-generic/fcntl.h:
    // ARM O_DIRECTORY is 040000 (octal); 0x10000 is O_DIRECT, not O_DIRECTORY.
    // O_RDONLY is zero and O_CLOEXEC is 02000000 for these supported ABIs.
    // Do not silently reuse an emulator ABI for an unqualified architecture.
    internal static int DirectoryOpenFlags(Architecture architecture) => architecture switch
    {
        Architecture.Arm or Architecture.Arm64 => 0x4000 | 0x80000,
        Architecture.X86 or Architecture.X64 => 0x10000 | 0x80000,
        _ => throw new PlatformNotSupportedException("Private directory sync requires a supported Android/Linux ABI.")
    };

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int Open(string path, int flags);
    [DllImport("libc", EntryPoint = "fsync", SetLastError = true)]
    private static extern int Fsync(int descriptor);
    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    private static extern int Close(int descriptor);
}
