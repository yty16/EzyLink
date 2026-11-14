using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;
using YiTongYao.Core;

namespace YiTongYao.Windows;

public partial class MainWindow : Window
{
    private readonly SettingsStore _settings;
    private readonly DiscoveryService _discovery;
    private RemoteServer? _server;
    private WinRtBluetoothServer? _bluetooth;
    private string _btMac = string.Empty;
    private bool _btStarting;
    private WinRtWiFiDirectServer? _wifiDirect;
    private string _wfdLastError = string.Empty;
    private NotifyIcon? _tray;

    // 推荐给设备连接的地址（移动热点地址优先）
    private string? _connectIp;
    private bool _reallyExit;
    private bool _forcedExit;   // 由重复启动的实例发出「重启当前实例」指令时置位，绕过认证强制退出
    private bool _busy;

    // 选项加载期间抑制 Checked/Unchecked 回调，避免初始化时反复写注册表
    private bool _loadingOptions;

    // 是否由「开机自动启动」拉起（命令行带 --startup）
    private readonly bool _startupLaunch;

    // 全局热键 Ctrl+Alt+Y：托盘图标隐藏时仍可唤出主窗口
    private IntPtr _hwnd;
    private const int HotkeyId = 9001;
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint VkY = 0x59;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern IntPtr GetThreadDpiAwarenessContext();
    [DllImport("user32.dll")]
    private static extern bool AreDpiAwarenessContextsEqual(IntPtr a, IntPtr b);
    [DllImport("user32.dll")]
    private static extern int GetAwarenessFromDpiAwarenessContext(IntPtr ctx);

    // 日志框最大保留行数，超出后裁掉前面部分，避免长时间运行后内存/渲染压力
    private const int MaxLogLines = 800;

    public MainWindow()
    {
        InitializeComponent();
        _startupLaunch = Environment.GetCommandLineArgs()
            .Any(a => string.Equals(a, AutoStart.StartupArg, StringComparison.OrdinalIgnoreCase));

        CrashLogger.LogRun("==== 程序启动 v" + AppConstants.Version +
                           (_startupLaunch ? "（开机自启）" : "") + " ====");
        _settings = SettingsStore.Load();
        bool tokenUpgraded = string.IsNullOrWhiteSpace(_settings.Token) || _settings.Token.Length < 8;
        if (tokenUpgraded)
        {
            _settings.Token = SettingsStore.GenerateToken();
            _settings.Save();
        }
        AuthService.Settings = _settings;
        _discovery = new DiscoveryService(Log);
        VersionText.Text = "EzyLink v" + AppConstants.Version;
        PortBox.Text = _settings.Port.ToString();
        TokenText.Text = _settings.Token;
        if (tokenUpgraded)
            Log("配对码已升级为 8 位数字（" + _settings.Token + "），请在手机 / 手表端重新扫码或更新配对码");

        LoadOptions();
        RefreshAuthUi();
        RefreshPrivacyUi();
        // 设置开启自启但注册表项缺失/路径变化时自动补回（例如被安全软件清理或重装到新目录）
        if (_settings.AutoLaunch && AutoStart.Sync(true))
            Log("已恢复开机自动启动项（注册表项缺失或路径已变化）");

        _ = RefreshNetworkAsync();
        SetupTray();
        ApplyStartupBehavior();

        SourceInitialized += Window_SourceInitialized;
        SourceInitialized += (_, __) => { App.FitWindow(this); LogDisplayInfo(); };

        Log($"启动模式：{(_startupLaunch ? "开机自启" : "手动启动")}" +
            $"；托盘图标：{(_settings.HideTrayIcon ? "隐藏" : "显示")}" +
            $"；自动开启服务：{(_settings.AutoStartServer ? "开" : "关")}");

        if (_settings.AutoStartServer)
            _ = SafeStartAsync();

        if (_settings.AutoCheckUpdate)
            _ = CheckUpdateSilentlyAsync();

        // 防火墙未放行时手机 / 手表根本连不上（表现为点了没反应），启动时自检一次并提示
        _ = Task.Run(() =>
        {
            if (!HotspotHelper.IsFirewallRulePresent())
                Log("提示：未检测到防火墙放行规则，手机 / 手表可能连不上本机。请点上方「防火墙放行」按钮（需管理员确认）。");
        });

        _ = InitializeBluetoothAsync();
        InitializeWiFiDirect();

        // 打开主窗口前过解锁闸门。
        bool willShowWindow = !(_startupLaunch && _settings.LaunchSilently);
        if (willShowWindow && !EnsureUnlocked(AuthOperation.OpenMain)) Hide();
    }

    // ---------- 显示环境 ----------

    /// <summary>启动日志记录缩放与 DPI 感知级别，便于高分屏 / 一体机现场排查。</summary>
    private void LogDisplayInfo()
    {
        try
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            IntPtr ctx = GetThreadDpiAwarenessContext();
            string mode = AreDpiAwarenessContextsEqual(ctx, new IntPtr(-4))
                ? "PerMonitorV2"
                : GetAwarenessFromDpiAwarenessContext(ctx) switch
                {
                    2 => "PerMonitor",
                    1 => "SystemAware",
                    0 => "Unaware",
                    _ => "Unknown"
                };
            var wa = SystemParameters.WorkArea;
            Log($"显示：缩放 {dpi.DpiScaleX * 100:0}%　DPI 感知 {mode}　工作区 {wa.Width:0}×{wa.Height:0}px");
        }
        catch { }
    }

    // ---------- 启动与托盘选项 ----------

    /// <summary>把已保存的设置回填到界面复选框。</summary>
    private void LoadOptions()
    {
        _loadingOptions = true;
        try
        {
            AutoLaunchBox.IsChecked = _settings.AutoLaunch;
            AutoStartServerBox.IsChecked = _settings.AutoStartServer;
            LaunchSilentBox.IsChecked = _settings.LaunchSilently;
            HideTrayBox.IsChecked = _settings.HideTrayIcon;
            MinimizeTrayBox.IsChecked = _settings.MinimizeToTray;
            // 二者互斥：任一项选中时禁用另一项，避免「隐藏托盘却最小化到托盘」导致无入口
            HideTrayBox.IsEnabled = !_settings.MinimizeToTray;
            MinimizeTrayBox.IsEnabled = !_settings.HideTrayIcon;
            BluetoothBox.IsChecked = _settings.BluetoothEnabled;
            WfdBox.IsChecked = _settings.WiFiDirectEnabled;
            BtPanel.Visibility = _settings.BluetoothEnabled ? Visibility.Visible : Visibility.Collapsed;
            WfdPanel.Visibility = _settings.WiFiDirectEnabled ? Visibility.Visible : Visibility.Collapsed;
        }
        finally
        {
            _loadingOptions = false;
        }
    }

    /// <summary>开机自启时按设置静默进入后台。</summary>
    private void ApplyStartupBehavior()
    {
        if (!_startupLaunch || !_settings.LaunchSilently) return;
        WindowState = WindowState.Minimized;
        // 按托盘可见性决定窗口是否隐藏。
        if (!_settings.HideTrayIcon) Hide();
    }

    // ---------- 安全认证 ----------

    private void RefreshAuthUi()
    {
        int n = _settings.AuthMethods.Count;
        LockBtn.IsEnabled = n > 0;
        if (n == 0)
        {
            AuthStatusText.Text = "尚未登记任何认证方式。点「认证方式管理」可添加密码、系统解锁（Windows Hello）或 U 盘。";
            return;
        }
        var parts = _settings.AuthMethods
            .Select(m => AuthMethodKinds.Label(m.Kind) + "（" +
                         (m.Operations.Count > 0
                             ? string.Join("、", m.Operations.Select(AuthOperation.Label))
                             : "未保护") + "）");
        AuthStatusText.Text = "已登记 " + n + " 种方式：" + string.Join("；", parts)
                              + (_settings.TwoFactorEnabled ? "。已启用双因子。" : "。");
    }

    // ---------- 蓝牙直连 ----------

    private async Task RefreshBluetoothAsync()
    {
        _btMac = await WinRtBluetoothInfo.LocalMacAsync();
        bool on = _bluetooth?.IsRunning == true;
        _discovery.SetBluetooth(on, _btMac, WinRtBluetoothServer.ServiceUuid.ToString());
        BtStatusText.Text = _btMac.Length == 0
            ? "未检测到蓝牙适配器，蓝牙通道不可用（WiFi 通道不受影响）。"
            : "本机蓝牙 MAC：" + _btMac + "　UUID：" + WinRtBluetoothServer.ServiceUuid
              + (on ? "　（通道已开启）" : "　（未开启）");
    }

    private async Task StartBluetoothIfNeeded()
    {
        if (!_settings.BluetoothEnabled || _bluetooth?.IsRunning == true || _btStarting) return;
        _btStarting = true;
        try
        {
            var bt = new WinRtBluetoothServer(_settings.Token, Log);
            if (!await bt.StartAsync()) { bt.Dispose(); return; }
            _bluetooth = bt;
            await RefreshBluetoothAsync();
        }
        finally { _btStarting = false; }
    }

    private async Task InitializeBluetoothAsync()
    {
        await RefreshBluetoothAsync();
        await StartBluetoothIfNeeded();
    }

    private void StopBluetooth()
    {
        try { _bluetooth?.Stop(); _bluetooth?.Dispose(); } catch { }
        _bluetooth = null;
        _discovery.SetBluetooth(false, _btMac, WinRtBluetoothServer.ServiceUuid.ToString());
        if (_btMac.Length > 0)
            BtStatusText.Text = "本机蓝牙 MAC：" + _btMac + "　（通道已关闭）";
    }

    // ---------- 连接方式（复选框多选）----------

    private async void BtOption_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingOptions) return;
        _settings.BluetoothEnabled = BluetoothBox.IsChecked == true;
        _settings.Save();
        if (BtPanel != null)
            BtPanel.Visibility = _settings.BluetoothEnabled ? Visibility.Visible : Visibility.Collapsed;
        if (_settings.BluetoothEnabled)
        {
            await StartBluetoothIfNeeded();
            if (_bluetooth?.IsRunning != true)
                BtStatusText.Text = "蓝牙通道启动失败，请确认系统蓝牙已开启（WiFi 通道不受影响）。";
            Log("已开启蓝牙通道");
        }
        else
        {
            StopBluetooth();
            Log("已关闭蓝牙通道（WiFi 通道不受影响）");
        }
    }

    private void WfdOption_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingOptions) return;
        _settings.WiFiDirectEnabled = WfdBox.IsChecked == true;
        _settings.Save();
        if (WfdPanel != null)
            WfdPanel.Visibility = _settings.WiFiDirectEnabled ? Visibility.Visible : Visibility.Collapsed;
        if (_settings.WiFiDirectEnabled)
        {
            StartWiFiDirect();
            if (_wifiDirect?.IsRunning != true)
                WfdStatusText.Text = "Wi-Fi Direct 通道启动失败：" + _wfdLastError + "（WiFi 通道不受影响）";
            Log("已开启 Wi-Fi Direct 通道");
        }
        else
        {
            StopWiFiDirect();
            Log("已关闭 Wi-Fi Direct 通道（WiFi 通道不受影响）");
        }
    }

    // ---------- Wi-Fi Direct 直连 ----------

    private void InitializeWiFiDirect()
    {
        if (_settings.WiFiDirectEnabled) StartWiFiDirect();
        else RefreshWfdUi();
    }

    private void StartWiFiDirect()
    {
        if (_wifiDirect?.IsRunning == true) return;
        var wfd = new WinRtWiFiDirectServer(_settings.Token, Log);
        wfd.StateChanged += () => Dispatcher.Invoke(RefreshWfdUi);
        if (!wfd.Start())
        {
            _wfdLastError = wfd.LastError;
            wfd.Dispose();
            RefreshWfdUi();
            return;
        }
        _wifiDirect = wfd;
        _wfdLastError = string.Empty;
        RefreshWfdUi();
    }

    private void StopWiFiDirect()
    {
        try { _wifiDirect?.Stop(); _wifiDirect?.Dispose(); } catch { }
        _wifiDirect = null;
        RefreshWfdUi();
    }

    private void RefreshWfdUi()
    {
        if (WfdStatusText == null) return;
        var wfd = _wifiDirect;
        bool on = wfd?.IsRunning == true;
        string ip = wfd?.LocalAddress ?? string.Empty;
        const string tip = "注意：Windows 移动热点与 Wi-Fi Direct 不能同时开启。";
        string pwdTip = (wfd?.Passphrase.Length ?? 0) < 8 && on
            ? "　提示：密码少于 8 位，部分设备会拒绝连接，届时把配对码改到 8 位以上即可。" : "";
        if (on)
        {
            WfdSsidText.Text = "网络：" + WinRtWiFiDirectServer.Ssid + "　密码：" + (wfd?.Passphrase ?? "");
            WfdStatusText.Text = "正在广播（" + (wfd?.PublisherStatus ?? "?") + "）"
                + (ip.Length > 0 ? "　本机地址：" + ip : "　地址将在设备接入后显示")
                + "　已连接 P2P 设备：" + (wfd?.ConnectionCount ?? 0)
                + "。手机 / 手表连上这个 WiFi 后，易通遥会自动搜索到电脑。" + pwdTip + tip;
        }
        else
        {
            WfdSsidText.Text = "网络：" + WinRtWiFiDirectServer.Ssid + "（未开启）";
            WfdStatusText.Text = "未开启"
                + (_wfdLastError.Length > 0 ? "（上次启动失败：" + _wfdLastError + "）" : "")
                + "。开启后本机会广播一个普通 WiFi 网络，手机 / 手表在系统 WiFi 设置里连上即可，连上后自动搜索到电脑。" + tip;
        }
    }

    private void WfdCopyBtn_Click(object sender, RoutedEventArgs e)
    {
        var wfd = _wifiDirect;
        if (wfd == null) { Log("请先启用 Wi-Fi Direct 热点，再复制热点信息。"); return; }
        try
        {
            System.Windows.Clipboard.SetText("网络名称：" + WinRtWiFiDirectServer.Ssid
                + "\n密码：" + wfd.Passphrase);
            Log("已复制 Wi-Fi Direct 热点名称与密码");
        }
        catch (Exception ex) { Log("复制失败：" + ex.Message); }
    }

    // ---------- 隐私与授权 ----------

    private void RefreshPrivacyUi()
    {
        bool ok = _settings.PrivacyAccepted;
        PrivacyRevokeBtn.Content = ok ? "撤回同意" : "重新同意";
        PrivacyStatusText.Text = ok
            ? "已同意《隐私政策》（" + (_settings.PrivacyAcceptedAt > DateTime.MinValue
                  ? _settings.PrivacyAcceptedAt.ToString("yyyy-MM-dd HH:mm") : "时间未记录")
              + "）。全部数据仅在本机与局域网内处理，不上传网络。"
            : "尚未同意《隐私政策》，远程控制服务不会运行。下次启动会重新征求你的同意。";
    }

    private void PrivacyPolicyBtn_Click(object sender, RoutedEventArgs e)
    {
        // 优先打开随程序发布的 LEGAL.md，缺失时回退到仓库地址
        if (!PrivacyConsentWindow.TryOpenLocalPrivacy())
            PrivacyConsentWindow.OpenUrl("https://github.com/" + AppConstants.GitHubOwner
                                         + "/" + AppConstants.GitHubRepo);
        Log("已打开隐私政策");
    }

    private void PrivacyRevokeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_settings.PrivacyAccepted)
        {
            var r = System.Windows.MessageBox.Show(this,
                "撤回同意后，远程控制服务将停止运行，下次启动会重新征求你的同意。\n\n" +
                "你的本机设置（IP、端口、配对码等）不会被删除，需要彻底清除请手动删除 " +
                "%LOCALAPPDATA%\\YiTongYao\\ 目录。\n\n确定撤回吗？",
                "易通遥 隐私与授权", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;

            if (_server?.IsRunning == true) _ = StopServer();
            _settings.PrivacyAccepted = false;
            _settings.PrivacyAcceptedAt = DateTime.MinValue;
            _settings.Save();
            RefreshPrivacyUi();
            Log("已撤回隐私政策同意，远程控制服务已停止");
        }
        else
        {
            // 拒绝后再次同意的路径：非强制模式，可关闭而不表态
            var dlg = new PrivacyConsentWindow(required: false) { Owner = this };
            dlg.ShowDialog();
            if (!dlg.Accepted) return;
            _settings.PrivacyAccepted = true;
            _settings.PrivacyAcceptedAt = DateTime.Now;
            _settings.Save();
            RefreshPrivacyUi();
            Log("已重新同意隐私政策");
        }
    }

    private void ManageBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureUnlocked(AuthOperation.Settings)) return;
        var win = new AuthMethodsWindow(_settings) { Owner = this };
        win.ShowDialog();
        RefreshAuthUi();
        Log("已更新认证方式（共 " + _settings.AuthMethods.Count + " 种）");
    }

    private void LockBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_settings.AuthMethods.Count == 0) return;
        // 立即作废当前会话并收起窗口，下次打开需重新验证
        AuthService.ResetSession();
        Hide();
        if (EnsureUnlocked(AuthOperation.OpenMain)) ShowWindow();
    }

    /// <summary>按操作弹出解锁闸门；无需认证或已解锁时直接放行。</summary>
    private bool EnsureUnlocked(string operation)
    {
        if (!AuthService.NeedsAuth(_settings, operation)) return true;
        if (AuthService.IsSessionActive(0)) return true;

        var win = new UnlockWindow(_settings, operation) { Owner = this, Topmost = true };
        if (win.ShowDialog() == true)
        {
            AuthService.SetSessionUnlocked();
            if (win.WantsPasswordReset)
            {
                var target = _settings.AuthMethods
                    .FirstOrDefault(m => m.Kind == AuthMethodKind.Password);
                var sp = new SetPasswordWindow(_settings, target) { Owner = this };
                sp.ShowDialog();
            }
            RefreshAuthUi();
            return true;
        }
        return false;
    }

    private void AutoLaunch_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingOptions) return;
        bool on = AutoLaunchBox.IsChecked == true;
        try
        {
            AutoStart.SetEnabled(on);
            _settings.AutoLaunch = on;
            _settings.Save();
            Log(on ? "已开启开机自动启动：下次登录 Windows 时会自动运行本程序"
                   : "已取消开机自动启动");
        }
        catch (Exception ex)
        {
            AutoLaunchBox.IsChecked = _settings.AutoLaunch;
            Log("开机自启设置失败：" + ex.Message);
        }
    }

    private void Option_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingOptions) return;
        _settings.AutoStartServer = AutoStartServerBox.IsChecked == true;
        _settings.LaunchSilently = LaunchSilentBox.IsChecked == true;
        _settings.MinimizeToTray = MinimizeTrayBox.IsChecked == true;
        _settings.Save();
        Log("设置已保存");
    }

    private void TrayOption_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingOptions) return;
        bool hide = HideTrayBox.IsChecked == true;
        bool minimize = MinimizeTrayBox.IsChecked == true;

        // 二者互斥：以最后操作的一方为准，自动取消另一项
        if (hide && minimize)
        {
            if (sender == HideTrayBox)
            {
                MinimizeTrayBox.IsChecked = false;
                minimize = false;
            }
            else
            {
                HideTrayBox.IsChecked = false;
                hide = false;
            }
        }

        _settings.HideTrayIcon = hide;
        _settings.MinimizeToTray = minimize;

        // 联动置灰：任一项被选中时禁用另一项，让用户一眼看出只能选其一
        HideTrayBox.IsEnabled = !minimize;
        MinimizeTrayBox.IsEnabled = !hide;

        _settings.Save();
        ApplyTrayVisibility();
        Log(hide ? "已隐藏系统托盘图标（Ctrl+Alt+Y 唤出主窗口，关闭即退出）"
                 : minimize ? "已开启关闭时最小化到托盘"
                 : "已恢复：关闭窗口直接退出，托盘图标显示");
    }

    private void ApplyTrayVisibility()
    {
        if (_tray == null) return;
        _tray.Visible = !_settings.HideTrayIcon;
        // 隐藏托盘时必须保证窗口可见，否则程序将无任何可操作入口
        if (_settings.HideTrayIcon && !IsVisible) ShowWindow();
    }

    // ---------- 全局热键 Ctrl+Alt+Y ----------

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(_hwnd)?.AddHook(HwndHook);
        if (!RegisterHotKey(_hwnd, HotkeyId, ModControl | ModAlt, VkY))
            Log("提示：Ctrl+Alt+Y 热键注册失败，可能已被其他程序占用");
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            ToggleWindow();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void ToggleWindow()
    {
        if (IsVisible && WindowState != WindowState.Minimized)
        {
            WindowState = WindowState.Minimized;
            if (_settings.MinimizeToTray && !_settings.HideTrayIcon) Hide();
            // 收起即作废当前解锁会话，下次打开主窗口需重新输入密码
            AuthService.ResetSession();
            Log("热键 Ctrl+Alt+Y：主窗口已收起");
        }
        else
        {
            ShowWindow();
            Log("热键 Ctrl+Alt+Y：主窗口已唤出");
        }
    }

    /// <summary>后台获取网络信息。</summary>
    private async Task RefreshNetworkAsync()
    {
        IpText.Text = "获取中…";
        MacText.Text = "获取中…";
        HostText.Text = "获取中…";

        var info = await Task.Run(() => (
            Ip: NetworkInfo.GetLocalIp(),
            Mac: NetworkInfo.GetMac(),
            Host: NetworkInfo.HostName,
            HotspotOn: HotspotHelper.IsHotspotOn(),
            HotspotIp: HotspotHelper.GetHotspotIp()));

        IpText.Text = info.Ip ?? "无法获取";
        MacText.Text = info.Mac ?? "无法获取";
        HostText.Text = info.Host;

        // 设备应连的地址：开了移动热点就优先用热点地址（离线直连场景）
        _connectIp = info.HotspotIp ?? info.Ip;
        HotspotText.Text = info.HotspotOn
            ? $"移动热点：已开启（{info.HotspotIp}）— 手机 / 手表连上这个热点即可离线直连，无需互联网"
            : "移动热点：未开启。与电脑同一个 WiFi 时可直接用；若要完全不依赖路由器，请点「开启移动热点设置」";
        UpdateConnectAddress();
    }

    private void UpdateConnectAddress()
    {
        int.TryParse(PortBox.Text, out int port);
        if (port <= 0 || port > 65535) port = _settings.Port;
        var addr = string.IsNullOrEmpty(_connectIp) ? IpText.Text : _connectIp;
        ConnectAddrText.Text = addr + ":" + port;
    }

    private void HotspotBtn_Click(object sender, RoutedEventArgs e)
    {
        HotspotHelper.OpenHotspotSettings();
        Log("已打开 Windows 移动热点设置页：开启并让手机 / 手表连上该热点后，" +
            "即可不依赖路由器与互联网直接控制电脑。开启后点「刷新」查看连接地址。");
    }

    private void FirewallBtn_Click(object sender, RoutedEventArgs e)
    {
        Log("正在申请添加防火墙入站放行规则（需管理员确认）…");
        // netsh 提权会弹 UAC 并可能等待较久，放到后台避免卡住界面
        _ = Task.Run(() =>
        {
            HotspotHelper.TryOpenFirewall(out var msg);
            Log(msg);
        });
    }

    private void RefreshBtn_Click(object sender, RoutedEventArgs e) => _ = RefreshNetworkAsync();

    private void QrBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_server?.IsRunning != true)
        {
            Log("请先启动服务，再使用扫码连接（二维码中包含当前端口与配对码）。");
            System.Windows.MessageBox.Show(this, "请先点击「启动服务」，再使用扫码连接。", "易通遥",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        int.TryParse(PortBox.Text, out int port);
        if (port <= 0 || port > 65535) port = _settings.Port;

        var ip = string.IsNullOrEmpty(_connectIp) ? IpText.Text : _connectIp;
        string btMac = _bluetooth?.IsRunning == true ? _btMac : string.Empty;
        var win = new QrWindow(ip, port, _settings.Token, btMac) { Owner = this };
        win.ShowDialog();
        Log($"已展示扫码连接二维码（{ip}:{port}），用手机 / 手表端「扫码连接」扫描即可自动连接。");
    }

    private void SetupTray()
    {
        _tray = new NotifyIcon();
        try
        {
            var info = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/vty_icon.ico"));
            if (info != null)
                _tray.Icon = new System.Drawing.Icon(info.Stream);
        }
        catch { }
        _tray.Text = "易通遥 远程控制";
        _tray.Visible = !_settings.HideTrayIcon;
        _tray.DoubleClick += (_, _) => ShowWindow();

        var menu = new ContextMenuStrip();
        var showItem = new ToolStripMenuItem("显示主窗口");
        showItem.Click += (_, _) => ShowWindow();
        var toggleItem = new ToolStripMenuItem("启动 / 停止服务");
        toggleItem.Click += (_, _) => ToggleBtn_Click(this, new RoutedEventArgs());
        var updateItem = new ToolStripMenuItem("检查更新");
        updateItem.Click += (_, _) => CheckUpdateBtn_Click(this, new RoutedEventArgs());
        var exitItem = new ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => { _reallyExit = true; Close(); };
        menu.Items.Add(showItem);
        menu.Items.Add(toggleItem);
        menu.Items.Add(updateItem);
        menu.Items.Add(exitItem);
        _tray.ContextMenuStrip = menu;
    }

    private void ShowWindow()
    {
        if (!EnsureUnlocked(AuthOperation.OpenMain)) return;
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    /// <summary>供重复启动的实例调用：把已有主窗口提到前台（公开给 App 的 IPC 回调）。</summary>
    internal void BringToFront() => ShowWindow();

    /// <summary>供重复启动的实例「重启当前实例」调用：绕过安全认证直接退出，让出单实例互斥体。</summary>
    internal void ForceClose()
    {
        _forcedExit = true;
        _reallyExit = true;
        Close();
    }

    /// <summary>启停服务（防重入）。</summary>
    private async void ToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        ToggleBtn.IsEnabled = false;
        try
        {
            if (_server?.IsRunning == true)
            {
                if (!EnsureUnlocked(AuthOperation.StopService))
                {
                    Log("停止服务已取消：未通过安全认证");
                    return;
                }
                await StopServer();
            }
            else
            {
                await StartServer();
            }
        }
        catch (Exception ex)
        {
            Log("操作异常：" + ex.Message);
        }
        finally
        {
            _busy = false;
            ToggleBtn.IsEnabled = true;
        }
    }

    /// <summary>自动启动服务并捕获异常。</summary>
    private async Task SafeStartAsync()
    {
        try { await StartServer(); }
        catch (Exception ex) { Log("自动启动服务失败：" + ex.Message); }
    }

    private async Task StartServer()
    {
        if (!int.TryParse(PortBox.Text, out int port) || port <= 0 || port > 65535)
        {
            Log("端口无效，请输入 1-65535 之间的数字");
            return;
        }
        Log($"准备启动服务（端口 {port}）…");

        _settings.Port = port;
        _settings.Token = TokenText.Text.Trim();
        _settings.Save();

        // 若存在残留实例先彻底释放，防止端口自占用
        if (_server != null)
        {
            if (_server.IsRunning) { Log("服务已在运行中"); return; }
            _server.Dispose();
            _server = null;
        }

        StatusText.Text = "服务启动中…";

        // 端口检测与监听绑定放到后台线程。
        var token = _settings.Token;
        RemoteServer? created = null;
        bool ok = false;
        string msg = "";

        await Task.Run(() =>
        {
            try
            {
                if (IsPortInUse(port))
                {
                    msg = $"端口 {port} 已被其他程序占用，请更换端口后重试" +
                          $"（常见占用：已运行的易通遥、其他远控或调试工具）";
                    return;
                }
                var srv = new RemoteServer(port, token, Log);
                srv.StartAsync().GetAwaiter().GetResult();
                created = srv;
                ok = true;
                msg = $"服务已启动，监听端口 {port}";
            }
            catch (SocketException ex)
            {
                msg = $"启动失败：端口 {port} 无法监听（{ex.SocketErrorCode}）。" +
                      $"请更换端口，或关闭占用该端口的程序后重试。";
            }
            catch (Exception ex)
            {
                msg = "启动失败：" + ex.Message;
            }
        });

        if (ok && created != null)
        {
            _server = created;
            _discovery.Start(port);
            UpdateConnectAddress();
            SetRunning(true);
            Log(msg);
            Log($"本机地址：{(_connectIp ?? IpText.Text)}:{port}");
            Log("等待手表 / 手机连接…");

            await RefreshBluetoothAsync();
            await StartBluetoothIfNeeded();
        }
        else
        {
            created?.Dispose();
            SetRunning(false);
            Log(msg);
        }
    }

    private Task StopServer()
    {
        StopBluetooth();
        StopWiFiDirect();
        SafeDisposeServer();
        _discovery.Stop();
        SetRunning(false);
        return Task.CompletedTask;
    }

    /// <summary>释放服务资源。</summary>
    private void SafeDisposeServer()
    {
        try
        {
            _server?.Stop();
            _server?.Dispose();
        }
        catch (Exception ex)
        {
            Log("停止服务时出错：" + ex.Message);
        }
        finally
        {
            _server = null;
        }
    }

    /// <summary>检测 TCP 端口是否被占用。</summary>
    private static bool IsPortInUse(int port)
    {
        try
        {
            var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
            foreach (var ep in listeners)
                if (ep.Port == port)
                    return true;
        }
        catch { }
        return false;
    }

    private void SetRunning(bool running)
    {
        StatusDot.Fill = running
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(54, 226, 232))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(136, 136, 136));
        StatusText.Text = running ? "服务运行中" : "服务未启动";
        ToggleBtn.Content = running ? "停止服务" : "启动服务";
    }

    private void RegenBtn_Click(object sender, RoutedEventArgs e)
    {
        _settings.Token = SettingsStore.GenerateToken();
        _settings.Save();
        TokenText.Text = _settings.Token;
        Log("已重新生成配对码");
    }

    private async void CheckUpdateBtn_Click(object sender, RoutedEventArgs e)
    {
        await DoCheckUpdateAsync(showNoUpdateDialog: true);
    }

    /// <summary>启动时静默检查更新。</summary>
    private async Task CheckUpdateSilentlyAsync()
    {
        try { await Task.Delay(4000); } catch { return; }
        await DoCheckUpdateAsync(showNoUpdateDialog: false);
    }

    private async Task DoCheckUpdateAsync(bool showNoUpdateDialog)
    {
        try
        {
            var info = await UpdateService.CheckAsync();

            if (!string.IsNullOrEmpty(info.Error))
            {
                Log("检查更新失败：" + info.Error);
                if (showNoUpdateDialog)
                    System.Windows.MessageBox.Show(
                        "检查更新失败：" + info.Error + "\n\n你也可以到项目发布页手动查看最新版本。",
                        "易通遥 检查更新", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!info.HasUpdate)
            {
                Log($"当前已是最新版本（v{UpdateService.CurrentVersion}）");
                if (showNoUpdateDialog)
                    System.Windows.MessageBox.Show(
                        $"当前已是最新版本（v{UpdateService.CurrentVersion}）。",
                        "易通遥 检查更新", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Log($"发现新版本 v{info.Version}，请在弹出窗口中确认是否更新");
            var win = new UpdateWindow(info) { Owner = this };
            win.ShowDialog();
        }
        catch (Exception ex)
        {
            Log("检查更新异常：" + ex.Message);
        }
    }

    /// <summary>打开「关于」窗口。</summary>
    private void AboutBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var win = new AboutWindow { Owner = this };
            win.ShowDialog();
        }
        catch (Exception ex)
        {
            Log("打开关于窗口异常：" + ex.Message);
        }
    }

    private void AppearanceBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var win = new AppearanceWindow { Owner = this };
            win.ShowDialog();
        }
        catch (Exception ex)
        {
            Log("打开外观设置异常：" + ex.Message);
        }
    }

    /// <summary>写日志（异步投递、行数限流）。</summary>
    private void Log(string msg)
    {
        var text = $"[{DateTime.Now:HH:mm:ss}] {msg}\n";
        // 同步写文件日志：界面日志看不到时（例如程序静默退出）也能追溯
        CrashLogger.LogRun(msg);
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                LogBox.AppendText(text);
                // 超出上限时裁掉前 200 行
                if (LogBox.LineCount > MaxLogLines)
                {
                    var lines = LogBox.Text.Split('\n');
                    LogBox.Text = string.Join("\n", lines.Skip(200));
                }
                LogBox.ScrollToEnd();
            }
            catch { }
        }));
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        // 托盘图标被隐藏时不隐藏窗口，否则程序将失去任务栏之外的唯一入口
        if (WindowState == WindowState.Minimized && _settings.MinimizeToTray && !_settings.HideTrayIcon)
        {
            Hide();
            // 收起即作废当前解锁会话，下次打开主窗口需重新输入密码
            AuthService.ResetSession();
        }
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // 托盘图标隐藏时关闭窗口即退出：此时没有托盘入口，继续驻留会导致程序无法唤出
        if (!_reallyExit && !_forcedExit && _settings.MinimizeToTray && !_settings.HideTrayIcon)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        // 退出前按认证方式验证。
        if (!_forcedExit && !EnsureUnlocked(AuthOperation.Exit))
        {
            e.Cancel = true;
            return;
        }

        CrashLogger.LogRun("==== 程序退出（用户关闭）====");
        try { if (_hwnd != IntPtr.Zero) UnregisterHotKey(_hwnd, HotkeyId); } catch { }
        _tray?.Dispose();
        StopBluetooth();
        StopWiFiDirect();
        SafeDisposeServer();
        _discovery.Stop();
        // 采用 OnExplicitShutdown：真正的退出必须由这里显式触发，否则收起的主窗口会让进程残留。
        System.Windows.Application.Current.Shutdown();
    }
}
