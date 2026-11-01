using System.Diagnostics;
using System.Runtime.InteropServices;

namespace YiTongYao.Core;

/// <summary>
/// 通过向 PowerPoint（或兼容软件）前台窗口发送标准快捷键来控制幻灯片放映。
/// 不依赖 Office 互操作组件，通用性更好。
/// </summary>
internal static class PptController
{
    // 虚拟键码
    private const byte VK_F5 = 0x74;
    private const byte VK_ESCAPE = 0x1B;
    private const byte VK_LEFT = 0x25;
    private const byte VK_RIGHT = 0x27;
    private const byte VK_PRIOR = 0x21; // PageUp
    private const byte VK_NEXT = 0x22;  // PageDown
    private const byte VK_SPACE = 0x20;
    private const byte VK_B = 0x42;
    private const byte VK_W = 0x57;

    private const uint KEYEVENTF_KEYUP = 0x0002;

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    private const int SW_RESTORE = 9;

    /// <summary>
    /// 尝试将 PowerPoint 主窗口置于前台，失败则退回当前前台窗口。
    /// </summary>
    private static void FocusPresentation()
    {
        IntPtr hwnd = FindWindow("PPTFrameClass", null);
        if (hwnd == IntPtr.Zero)
        {
            // 退而求其次：按标题包含 PowerPoint 的窗口查找
            foreach (var p in Process.GetProcessesByName("POWERPNT"))
            {
                if (p.MainWindowHandle != IntPtr.Zero) { hwnd = p.MainWindowHandle; break; }
            }
        }
        if (hwnd == IntPtr.Zero) return; // 没有 PPT 窗口，直接用当前前台

        IntPtr fg = GetForegroundWindow();
        if (fg != hwnd)
        {
            uint targetThread = GetWindowThreadProcessId(hwnd, out _);
            uint curThread = GetCurrentThreadId();
            if (targetThread != curThread)
                AttachThreadInput(curThread, targetThread, true);
            ShowWindow(hwnd, SW_RESTORE);
            SetForegroundWindow(hwnd);
            if (targetThread != curThread)
                AttachThreadInput(curThread, targetThread, false);
        }
    }

    private static void SendKey(byte vk)
    {
        keybd_event(vk, 0, 0, UIntPtr.Zero);
        keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    public static void Start() { FocusPresentation(); SendKey(VK_F5); }
    public static void Begin() { FocusPresentation(); SendKey(VK_F5); }
    public static void Next() { FocusPresentation(); SendKey(VK_RIGHT); }
    public static void Prev() { FocusPresentation(); SendKey(VK_LEFT); }
    public static void Exit() { FocusPresentation(); SendKey(VK_ESCAPE); }
    public static void Black() { FocusPresentation(); SendKey(VK_B); }
    public static void White() { FocusPresentation(); SendKey(VK_W); }
}
