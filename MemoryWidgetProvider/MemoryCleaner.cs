using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MemoryWidgetProvider;

internal readonly record struct MemoryCleanupResult(
    int AttemptedProcessCount,
    int SucceededProcessCount,
    long ReleasedBytes,
    long DurationMs);

internal static class MemoryCleaner
{
    private const uint ProcessSetQuota = 0x0100;
    private const uint ProcessQueryLimitedInformation = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EmptyWorkingSet(IntPtr processHandle);

    public static MemoryCleanupResult TrimWorkingSets()
    {
        var stopwatch = Stopwatch.StartNew();
        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            throw new InvalidOperationException($"枚举进程失败：{ex.Message}", ex);
        }

        var attemptedProcessCount = 0;
        var succeededProcessCount = 0;
        long releasedBytes = 0;

        try
        {
            foreach (var process in processes)
            {
                if (!CanAttemptProcess(process))
                {
                    continue;
                }

                attemptedProcessCount++;

                var beforeBytes = ReadWorkingSetSafe(process);
                var isTrimmed = TryEmptyWorkingSet(process.Id);
                if (!isTrimmed)
                {
                    continue;
                }

                succeededProcessCount++;
                var afterBytes = ReadWorkingSetSafe(process);
                if (beforeBytes > afterBytes && afterBytes >= 0)
                {
                    releasedBytes += beforeBytes - afterBytes;
                }
            }
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }

        stopwatch.Stop();
        return new MemoryCleanupResult(
            attemptedProcessCount,
            succeededProcessCount,
            releasedBytes,
            stopwatch.ElapsedMilliseconds);
    }

    private static bool CanAttemptProcess(Process process)
    {
        try
        {
            return process.Id > 4 && !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryEmptyWorkingSet(int processId)
    {
        var handle = IntPtr.Zero;

        try
        {
            handle = OpenProcess(ProcessSetQuota | ProcessQueryLimitedInformation, false, processId);
            if (handle == IntPtr.Zero)
            {
                return false;
            }

            return EmptyWorkingSet(handle);
        }
        catch
        {
            return false;
        }
        finally
        {
            if (handle != IntPtr.Zero)
            {
                CloseHandle(handle);
            }
        }
    }

    private static long ReadWorkingSetSafe(Process process)
    {
        try
        {
            process.Refresh();
            return process.WorkingSet64;
        }
        catch
        {
            return -1;
        }
    }
}
