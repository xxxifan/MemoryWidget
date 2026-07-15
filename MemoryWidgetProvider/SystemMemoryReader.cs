using System.Runtime.InteropServices;

namespace MemoryWidgetProvider;

internal readonly record struct MemorySnapshot(ulong TotalBytes, ulong AvailableBytes, int UsedPercentage)
{
    public ulong UsedBytes => TotalBytes - AvailableBytes;
}

internal static class SystemMemoryReader
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

    public static MemorySnapshot Read()
    {
        var status = new MemoryStatusEx
        {
            dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>()
        };

        if (!GlobalMemoryStatusEx(ref status))
        {
            throw new InvalidOperationException("读取系统内存状态失败。");
        }

        return new MemorySnapshot(status.ullTotalPhys, status.ullAvailPhys, (int)status.dwMemoryLoad);
    }
}
