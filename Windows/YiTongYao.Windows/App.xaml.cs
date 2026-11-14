using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace YiTongYao.Windows;

public partial class App : System.Windows.Application
{
    /// <summary>
    /// 全局异常兜底：任何未捕获异常都写入崩溃日志并弹出提示，
    /// 不再让程序「未响应后直接消失」（旧版没有任何异常处理，异常即崩溃退出）。
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        // 托盘类应用：收起主窗口（例如「打开主窗口时要求密码」未解锁、或隐藏托盘图标）后
        // 程序仍需存活以保留热键 / 托盘入口，因此采用「显式退出」模式，只有明确的退出路径才结束进程。
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        base.OnStartup(e);

        // 所有窗口显示前按当前屏幕 / DPI 收进工作区，避免小屏或高缩放下被裁切
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => { if (sender is Window w) FitWindow(w); }));

        // ---- 单实例互斥：已有实例在运行时，通知它把窗口提到前台并弹出「已在运行」提示 ----
        if (!YiTongYao.Core.SingleInstance.TryBecomePrimary())
        {
            YiTongYao.Core.SingleInstance.Signal("SHOW");
            bool restart = false;
            try
            {
                var dlg = new AlreadyRunningWindow();
                restart = dlg.ShowDialog() == true;
            }
            catch { }

            if (restart)
            {
                // 先通知主实例退出并释放互斥体，再等待本实例接管
                YiTongYao.Core.SingleInstance.Signal("RESTART");
                if (!WaitBecomePrimary())
                {
                    CrashLogger.LogRun("重启失败：无法接管单实例互斥体");
                    Shutdown();
                    return;
                }
                // 接管成功，继续往下以主实例身份启动
            }
            else
            {
                Shutdown();
                return;
            }
        }

        // 主实例：启动 IPC 监听（重复启动时用于把窗口提到前台 / 接收重启指令）
        YiTongYao.Core.SingleInstance.StartServer(
            onShow: () => Dispatcher.BeginInvoke(() => (_mainWindow as MainWindow)?.BringToFront()),
            onRestart: () => Dispatcher.BeginInvoke(() => (_mainWindow as MainWindow)?.ForceClose()));
        Exit += (_, _) => YiTongYao.Core.SingleInstance.Release();

        // 隐私同意闸门：首次启动（或已撤回同意）时先征求同意，未同意则不创建主窗口直接退出。
        // 注意 StartupUri 已移除，主窗口在此处显式创建，确保闸门先于任何服务 / 托盘初始化。
        var s = YiTongYao.Core.SettingsStore.Load();
        if (!s.PrivacyAccepted)
        {
            var dlg = new PrivacyConsentWindow(required: true);
            dlg.ShowDialog();
            if (!dlg.Accepted)
            {
                CrashLogger.LogRun("用户未同意隐私政策，程序退出");
                Shutdown();
                return;
            }
            s.PrivacyAccepted = true;
            s.PrivacyAcceptedAt = DateTime.Now;
            s.Save();
            CrashLogger.LogRun("已同意隐私政策");
        }

        _mainWindow = new MainWindow();
        _mainWindow.Show();
    }

    private MainWindow? _mainWindow;

    /// <summary>
    /// 轮询等待成为主实例（主实例退出并释放互斥体后本实例才能接管）。最多约 6 秒。
    /// </summary>
    private static bool WaitBecomePrimary()
    {
        for (int i = 0; i < 60; i++)
        {
            if (YiTongYao.Core.SingleInstance.TryBecomePrimary()) return true;
            System.Threading.Thread.Sleep(100);
        }
        return false;
    }

    /// <summary>把窗口限制在当前屏幕工作区内，超出时按比例缩小并移回可见范围。</summary>
    public static void FitWindow(Window w)
    {
        try
        {
            // 用窗口所在显示器的工作区；SystemParameters.WorkArea 只返回主屏，多屏下会把副屏窗口拉回主屏
            Rect area;
            try
            {
                var wa = System.Windows.Forms.Screen.FromHandle(
                    new System.Windows.Interop.WindowInteropHelper(w).Handle).WorkingArea;
                area = new Rect(wa.Left, wa.Top, wa.Width, wa.Height);
            }
            catch { area = SystemParameters.WorkArea; }

            var dpi = VisualTreeHelper.GetDpi(w);
            double left = area.Left / dpi.DpiScaleX;
            double top = area.Top / dpi.DpiScaleY;
            double width = area.Width / dpi.DpiScaleX;
            double height = area.Height / dpi.DpiScaleY;

            // 上限取工作区整幅，最大化时才不会因上限偏小而四周留白
            w.MaxWidth = Math.Max(w.MinWidth, width);
            w.MaxHeight = Math.Max(w.MinHeight, height);
            if (w.WindowState == WindowState.Maximized) return;

            double maxW = Math.Max(w.MinWidth, width - 24);
            double maxH = Math.Max(w.MinHeight, height - 24);
            if (!double.IsNaN(w.Width) && w.Width > maxW) w.Width = maxW;
            if (!double.IsNaN(w.Height) && w.Height > maxH) w.Height = maxH;

            if (!double.IsNaN(w.Left) && (w.Left < left || w.Left + w.Width > left + width))
                w.Left = Math.Max(left, left + (width - w.Width) / 2);
            if (!double.IsNaN(w.Top) && (w.Top < top || w.Top + w.Height > top + height))
                w.Top = Math.Max(top, top + (height - w.Height) / 2);
        }
        catch { }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            var path = CrashLogger.Write(e.Exception);
            System.Windows.MessageBox.Show(
                "程序遇到一个未处理的错误，已记录到日志，你可以继续使用或重新启动程序。\n\n" +
                e.Exception.Message + "\n\n日志位置：\n" + path,
                "易通遥 运行时错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            e.Handled = true; // 标记已处理，防止进程直接退出
        }
        catch
        {
            e.Handled = true;
        }
    }

    private void OnDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        try
        {
            if (e.ExceptionObject is Exception ex)
                CrashLogger.Write(ex);
        }
        catch { }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        try { CrashLogger.Write(e.Exception); } catch { }
        e.SetObserved(); // 避免后台任务异常在 GC 时终止进程
    }
}

/// <summary>
/// 崩溃日志写入器：保存到 %LOCALAPPDATA%\YiTongYao\logs\crash-yyyyMMdd.log
/// </summary>
internal static class CrashLogger
{
    public static string DirectoryPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "YiTongYao", "logs");

    /// <summary>
    /// 运行日志（与界面日志同步写入），用于排查「程序看起来没反应 / 自己退出」这类问题。
    /// </summary>
    public static void LogRun(string msg)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            var file = Path.Combine(DirectoryPath, "app-" + DateTime.Now.ToString("yyyyMMdd") + ".log");
            File.AppendAllText(file,
                "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + msg + "\n");
        }
        catch { }
    }

    public static string Write(Exception ex)
    {
        var head = "==== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ====\n" +
                   "Version: " + YiTongYao.Core.AppConstants.Version + "\n" +
                   "OS: " + Environment.OSVersion + "\n";
        LogRun("!! 未处理异常: " + ex.Message);
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            var file = Path.Combine(DirectoryPath, "crash-" + DateTime.Now.ToString("yyyyMMdd") + ".log");
            File.AppendAllText(file, head + ex + "\n\n");
            return file;
        }
        catch
        {
            return "(日志写入失败)";
        }
    }
}
