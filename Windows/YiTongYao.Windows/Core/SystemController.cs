using System.Diagnostics;
using System.Runtime.InteropServices;

namespace YiTongYao.Core;

/// <summary>系统级操作：锁屏、睡眠、休眠、关机、重启。</summary>
internal static class SystemController
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LockWorkStation();

    [DllImport("powrprof.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    public static void Lock()
    {
        LockWorkStation();
    }

    public static void Sleep()
    {
        // hibernate=false -> 睡眠（待机）
        SetSuspendState(false, true, false);
    }

    public static void Hibernate()
    {
        SetSuspendState(true, true, false);
    }

    public static void Shutdown()
    {
        RunShutdown("/s");
    }

    public static void Restart()
    {
        RunShutdown("/r");
    }

    private static void RunShutdown(string action)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "shutdown.exe",
            Arguments = $"{action} /f /t 0",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        Process.Start(psi);
    }
}
