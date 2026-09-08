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
        int descriptor = Open(directory, 0x10000 | 0x80000); // O_DIRECTORY | O_CLOEXEC
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

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int Open(string path, int flags);
    [DllImport("libc", EntryPoint = "fsync", SetLastError = true)]
    private static extern int Fsync(int descriptor);
    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    private static extern int Close(int descriptor);
}
