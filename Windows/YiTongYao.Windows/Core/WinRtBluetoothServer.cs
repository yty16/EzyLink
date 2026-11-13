using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Networking.Sockets;

namespace YiTongYao.Core;

/// <summary>蓝牙 RFCOMM 服务端（WinRT），与 TCP 服务共用同一套会话层协议。</summary>
internal sealed class WinRtBluetoothServer : IDisposable
{
    /// <summary>移动端扫码 / 发现后据此 UUID 建立 RFCOMM 连接；两端必须一致。</summary>
    public static readonly Guid ServiceUuid = new("6E8F3A21-5C7B-4D9E-A1F2-0B3C4D5E6F70");

    private readonly string _token;
    private readonly Action<string> _log;
    private RfcommServiceProvider? _provider;
    private StreamSocketListener? _listener;
    private CancellationTokenSource? _cts;

    public bool IsRunning { get; private set; }
    public string LastError { get; private set; } = string.Empty;

    public WinRtBluetoothServer(string token, Action<string> log)
    {
        _token = token;
        _log = log;
    }

    /// <summary>启动与停止</summary>
    public async Task<bool> StartAsync()
    {
        if (IsRunning) return true;
        LastError = string.Empty;
        _cts = new CancellationTokenSource();
        try
        {
            var id = RfcommServiceId.FromUuid(ServiceUuid);
            _provider = await RfcommServiceProvider.CreateAsync(id);
            _listener = new StreamSocketListener();
            _listener.ConnectionReceived += OnConnectionReceived;
            await _listener.BindServiceNameAsync(id.AsString(),
                SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication);
            _provider.StartAdvertising(_listener);
            IsRunning = true;
            _log("蓝牙通道已启动（RFCOMM），UUID " + ServiceUuid);
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _log("蓝牙通道启动失败：" + ex.Message + "（WiFi 通道不受影响）");
            Stop();
            return false;
        }
    }

    /// <summary>接入会话</summary>
    private void OnConnectionReceived(StreamSocketListener sender,
                                      StreamSocketListenerConnectionReceivedEventArgs args)
    {
        var socket = args.Socket;
        var stream = new WinRtSocketStream(socket);
        var ct = _cts?.Token ?? CancellationToken.None;
        _ = SessionRunner.RunAsync(stream, "蓝牙设备", _token, ct, _log)
                         .ContinueWith(_ => { try { stream.Dispose(); } catch { } });
    }

    public void Stop()
    {
        IsRunning = false;
        try { _provider?.StopAdvertising(); } catch { }
        try
        {
            if (_listener != null) _listener.ConnectionReceived -= OnConnectionReceived;
        }
        catch { }
        try { _listener?.Dispose(); } catch { }
        _listener = null;
        _provider = null;
        try { _cts?.Cancel(); } catch { }
        try { _cts?.Dispose(); } catch { }
        _cts = null;
    }

    public void Dispose() => Stop();
}
