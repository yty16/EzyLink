using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Windows.Devices.Enumeration;
using Windows.Devices.WiFiDirect;

namespace YiTongYao.Core;

/// <summary>Wi-Fi Direct 服务端（WinRT）：广播 P2P 组并接受移动端连接。</summary>
internal sealed class WinRtWiFiDirectServer : IDisposable
{
    /// <summary>广播的 SSID；手机 / 手表在系统 WiFi 设置里连这个网络。</summary>
    public const string Ssid = "EzyLink-Direct";

    private readonly string _token;
    private readonly Action<string> _log;
    private WiFiDirectAdvertisementPublisher? _publisher;
    private WiFiDirectConnectionListener? _listener;
    private readonly List<WiFiDirectDevice> _devices = new();
    private readonly object _lock = new();

    public bool IsRunning { get; private set; }
    public string LastError { get; private set; } = string.Empty;
    public string LocalAddress { get; private set; } = string.Empty;
    public string Passphrase { get; private set; } = string.Empty;
    public string PublisherStatus => _publisher?.Status.ToString() ?? "Stopped";

    public int ConnectionCount
    {
        get { lock (_lock) return _devices.Count; }
    }

    public event Action? StateChanged;

    public WinRtWiFiDirectServer(string token, Action<string> log)
    {
        _token = string.IsNullOrEmpty(token) ? SettingsStore.GenerateToken() : token;
        _log = log;
    }

    /// <summary>启动与停止</summary>
    public bool Start()
    {
        if (IsRunning) return true;
        LastError = string.Empty;
        try
        {
            Passphrase = _token;
            var pub = new WiFiDirectAdvertisementPublisher();
            pub.Advertisement.IsAutonomousGroupOwnerEnabled = true;
            pub.Advertisement.ListenStateDiscoverability =
                WiFiDirectAdvertisementListenStateDiscoverability.Normal;

            // Legacy AP：把 P2P 组同时广播成普通 WiFi 网络，移动端无需 P2P 权限即可接入
            var legacy = pub.Advertisement.LegacySettings;
            legacy.IsEnabled = true;
            legacy.Ssid = Ssid;
            var cred = new global::Windows.Security.Credentials.PasswordCredential();
            cred.Password = Passphrase;
            legacy.Passphrase = cred;

            pub.StatusChanged += OnStatusChanged;

            var listener = new WiFiDirectConnectionListener();
            listener.ConnectionRequested += OnConnectionRequested;

            _publisher = pub;
            _listener = listener;
            pub.Start();

            IsRunning = true;
            LocalAddress = FindP2pAddress();
            _log("Wi-Fi Direct 通道已启动：热点 " + Ssid + "，密码 " + Passphrase
                 + (LocalAddress.Length > 0 ? "，本机地址 " + LocalAddress : ""));
            if (Passphrase.Length < 8)
                _log("提示：热点密码为 " + Passphrase.Length + " 位，部分设备要求 WiFi 密码至少 8 位；连不上时把配对码改长即可。");
            StateChanged?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _log("Wi-Fi Direct 通道启动失败：" + ex.Message + "（WiFi 通道不受影响）");
            Stop();
            return false;
        }
    }

    private void OnStatusChanged(WiFiDirectAdvertisementPublisher sender,
                                 WiFiDirectAdvertisementPublisherStatusChangedEventArgs args)
    {
        try { StateChanged?.Invoke(); } catch { }
    }

    /// <summary>接入会话：自动接受 P2P 请求，应用层配对码仍单独校验</summary>
    private async void OnConnectionRequested(WiFiDirectConnectionListener sender,
                                             WiFiDirectConnectionRequestedEventArgs args)
    {
        try
        {
            WiFiDirectConnectionRequest req = args.GetConnectionRequest();
            DeviceInformation info = req.DeviceInformation;
            string id = info?.Id ?? string.Empty;
            if (id.Length == 0) return;

            var device = await WiFiDirectDevice.FromIdAsync(id);
            if (device == null)
            {
                _log("Wi-Fi Direct 连接未完成（对端可能取消了配对）");
                return;
            }

            lock (_lock) _devices.Add(device);
            device.ConnectionStatusChanged += (s, e) =>
            {
                lock (_lock) _devices.Remove(device);
                try { StateChanged?.Invoke(); } catch { }
            };

            foreach (var ep in device.GetConnectionEndpointPairs())
            {
                string ip = ep.LocalHostName?.DisplayName ?? string.Empty;
                if (ip.Length == 0 || ip.Contains(":")) continue;
                LocalAddress = ip;
                break;
            }

            _log("Wi-Fi Direct 设备已连接：" + (info?.Name ?? "?")
                 + "，本机 P2P 地址 " + (LocalAddress.Length > 0 ? LocalAddress : "未知"));
            try { StateChanged?.Invoke(); } catch { }
        }
        catch (Exception ex)
        {
            _log("Wi-Fi Direct 接受连接失败：" + ex.Message);
        }
    }

    /// <summary>未建立连接时，从 Wi-Fi Direct 虚拟网卡上取本机地址</summary>
    private static string FindP2pAddress()
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                string desc = (ni.Description ?? "") + " " + (ni.Name ?? "");
                if (desc.IndexOf("Wi-Fi Direct", StringComparison.OrdinalIgnoreCase) < 0
                    && desc.IndexOf("WiFiDirect", StringComparison.OrdinalIgnoreCase) < 0) continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    string ip = ua.Address.ToString();
                    if (ip == "127.0.0.1" || ip.StartsWith("169.254.")) continue;
                    return ip;
                }
            }
        }
        catch { }
        return string.Empty;
    }

    public void Stop()
    {
        IsRunning = false;
        try { if (_publisher != null) _publisher.StatusChanged -= OnStatusChanged; } catch { }
        try { _publisher?.Stop(); } catch { }
        try { if (_listener != null) _listener.ConnectionRequested -= OnConnectionRequested; } catch { }
        lock (_lock) _devices.Clear();
        _publisher = null;
        _listener = null;
        LocalAddress = string.Empty;
        try { StateChanged?.Invoke(); } catch { }
    }

    public void Dispose() => Stop();
}
