using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MemoryCleaner;

internal sealed class MemoryService
{
    private int _cleanupInProgress;

    private static readonly HashSet<string> ProtectedProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "idle", "system", "registry", "memory compression",
        "smss", "csrss", "wininit", "services", "lsass", "lsaiso", "winlogon",
        "fontdrvhost", "dwm", "audiodg", "secure system",
        "explorer", "sihost", "taskhostw", "runtimebroker", "searchhost",
        "searchindexer", "startmenuexperiencehost", "shellexperiencehost"
    };

    internal MemorySnapshot ReadMemory()
    {
        var status = NativeMethods.MemoryStatusEx.Create();
        if (!NativeMethods.GlobalMemoryStatusEx(ref status))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取系统内存状态。");
        }

        return new MemorySnapshot(status.TotalPhysical, status.AvailablePhysical, status.MemoryLoad);
    }

    internal CleanupResult TrimUserProcesses()
    {
        if (Interlocked.CompareExchange(ref _cleanupInProgress, 1, 0) != 0)
        {
            throw new InvalidOperationException("已有内存整理任务正在进行。");
        }

        try
        {
            return TrimUserProcessesCore();
        }
        finally
        {
            Volatile.Write(ref _cleanupInProgress, 0);
        }
    }

    private static CleanupResult TrimUserProcessesCore()
    {
        int currentProcessId = Environment.ProcessId;
        int currentSessionId = Process.GetCurrentProcess().SessionId;
        var result = new CleanupResult();

        foreach (Process process in Process.GetProcesses())
        {
            using (process)
            {
                string name = SafeProcessName(process);

                try
                {
                    if (ShouldSkip(process, name, currentProcessId, currentSessionId))
                    {
                        result.Skipped++;
                        continue;
                    }

                    TrimOneProcess(process.Id, name, result);
                }
                catch (InvalidOperationException)
                {
                    result.Exited++;
                }
                catch (ArgumentException)
                {
                    result.Exited++;
                }
                catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
                {
                    result.AccessDenied++;
                    result.AddFailure($"{name}：权限不足");
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    result.AddFailure($"{name}：{ex.Message}");
                }
            }
        }

        return result;
    }

    private static bool ShouldSkip(
        Process process,
        string name,
        int currentProcessId,
        int currentSessionId)
    {
        if (process.Id <= 4 || process.Id == currentProcessId)
        {
            return true;
        }

        if (ProtectedProcessNames.Contains(name))
        {
            return true;
        }

        // 只处理当前交互式登录会话，避免触碰服务和其他用户的进程。
        return process.SessionId != currentSessionId;
    }

    private static void TrimOneProcess(int processId, string name, CleanupResult result)
    {
        IntPtr handle = NativeMethods.OpenProcess(
            NativeMethods.ProcessQueryInformation | NativeMethods.ProcessSetQuota,
            false,
            processId);

        if (handle == IntPtr.Zero)
        {
            int error = Marshal.GetLastWin32Error();
            if (error == 5)
            {
                result.AccessDenied++;
                result.AddFailure($"{name}：权限不足");
            }
            else if (error is 87 or 1168)
            {
                result.Exited++;
            }
            else
            {
                result.Failed++;
                result.AddFailure($"{name}：无法打开进程（错误 {error}）");
            }

            return;
        }

        try
        {
            // EmptyWorkingSet 是首选；旧系统或个别进程不支持时再用等价方式回退。
            if (NativeMethods.EmptyWorkingSet(handle) ||
                NativeMethods.SetProcessWorkingSetSize(handle, new IntPtr(-1), new IntPtr(-1)))
            {
                result.Succeeded++;
                return;
            }

            int error = Marshal.GetLastWin32Error();
            if (error == 5)
            {
                result.AccessDenied++;
                result.AddFailure($"{name}：权限不足");
            }
            else
            {
                result.Failed++;
                result.AddFailure($"{name}：整理失败（错误 {error}）");
            }
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    private static string SafeProcessName(Process process)
    {
        try
        {
            return process.ProcessName;
        }
        catch
        {
            return $"PID {process.Id}";
        }
    }
}

internal readonly record struct MemorySnapshot(ulong TotalBytes, ulong AvailableBytes, uint LoadPercent)
{
    internal ulong UsedBytes => TotalBytes - AvailableBytes;
}

internal sealed class CleanupResult
{
    private const int MaxFailureDetails = 12;

    internal int Succeeded { get; set; }
    internal int Skipped { get; set; }
    internal int AccessDenied { get; set; }
    internal int Exited { get; set; }
    internal int Failed { get; set; }
    internal List<string> FailureDetails { get; } = new();

    internal void AddFailure(string message)
    {
        if (FailureDetails.Count < MaxFailureDetails)
        {
            FailureDetails.Add(message);
        }
    }
}
