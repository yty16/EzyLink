using System.Net;
using System.Net.Sockets;

namespace YiTongYao.Core;

/// <summary>TCP 远控服务端：监听端口并接受连接。</summary>
internal sealed class RemoteServer : IDisposable
{
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly int _port;
    private readonly string _token;
    private readonly Action<string>? _log;

    public bool IsRunning { get; private set; }

    public RemoteServer(int port, string token, Action<string>? log = null)
    {
        _port = port;
        _token = token ?? string.Empty;
        _log = log;
    }

    /// <summary>启动监听。</summary>
    public Task StartAsync()
    {
        if (IsRunning) return Task.CompletedTask;

        var listener = new TcpListener(IPAddress.Any, _port);
        // 允许端口被 TIME_WAIT 中的连接快速复用，减少重启服务时误报“端口占用”
        try { listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true); }
        catch { }
        listener.Start();

        _listener = listener;
        _cts = new CancellationTokenSource();
        IsRunning = true;
        _log?.Invoke($"服务已启动，监听端口 {_port}");
        _ = AcceptLoopAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public void Stop()
    {
        if (!IsRunning) return;
        IsRunning = false;
        try { _cts?.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }
        _log?.Invoke("服务已停止");
    }

    /// <summary>接受连接循环。</summary>
    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener!.AcceptTcpClientAsync(ct);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }       // listener 已释放（停止）
            catch (InvalidOperationException) { break; }     // listener 未启动
            catch (SocketException)
            {
                // 监听器已停止或底层套接字失效，继续循环只会空转
                break;
            }
            catch (Exception ex)
            {
                _log?.Invoke("接受连接失败: " + ex.Message);
                if (ct.IsCancellationRequested) break;
                // 未知异常：退避后再试，绝不无间隔空转
                try { await Task.Delay(500, ct); } catch { break; }
                continue;
            }
            _ = HandleClientAsync(client, ct);
        }
    }

    /// <summary>处理一条 TCP 连接。</summary>
    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        var remote = client.Client.RemoteEndPoint?.ToString() ?? "?";
        try
        {
            // 让会话层的同步阻塞读能周期性返回以响应「停止服务」
            client.ReceiveTimeout = SessionRunner.ReadTimeoutMs;
            await SessionRunner.RunAsync(client.GetStream(), remote, _token, ct, _log);
        }
        catch (Exception ex)
        {
            _log?.Invoke($"客户端 {remote} 建立会话失败: {ex.Message}");
        }
        finally
        {
            try { client.Dispose(); } catch { }
        }
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
