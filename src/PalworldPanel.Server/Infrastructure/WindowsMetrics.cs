using System.Runtime.InteropServices;

namespace PalworldPanel.Server.Infrastructure;

internal static class WindowsMetrics
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtended;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime { public uint Low, High; public readonly long Ticks => (long)(((ulong)High << 32) | Low); }
    public static (long? Total, long? Available, long? Ticks, long? Idle) Read()
    {
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        var validMemory = GlobalMemoryStatusEx(ref memory);
        var validCpu = GetSystemTimes(out var idle, out var kernel, out var user);
        return (validMemory ? (long)memory.TotalPhysical : null, validMemory ? (long)memory.AvailablePhysical : null,
            validCpu ? kernel.Ticks + user.Ticks : null, validCpu ? idle.Ticks : null);
    }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);
}
