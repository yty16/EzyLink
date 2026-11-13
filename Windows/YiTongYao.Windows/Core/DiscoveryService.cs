using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace YiTongYao.Core;

/// <summary>
/// UDP 广播自动发现服务。
///
/// 解决的问题：手机 / 手表要连电脑，必须知道电脑 IP；而家庭/公司/热点环境下 IP 会变，
/// 手填 IP 很容易失效（表现为「点了按钮没反应」）。
///
/// 机制：手机端向 255.255.255.255:<see cref="AppConstants.DiscoveryPort"/> 广播
/// <see cref="AppConstants.DiscoveryRequest"/>，本服务收到后回复本机的主机名、TCP 端口
/// 与候选 IP 列表；与请求方同网段的地址排在最前，客户端依次尝试即可连通。
/// 该过程完全在局域网内完成，不需要互联网。
/// </summary>
internal sealed class DiscoveryService : IDisposable
{
    private UdpClient? _udp;
    private CancellationTokenSource? _cts;
    private readonly Action<string>? _log;
    private int _tcpPort;
    private bool _btAvailable;
    private string _btMac = string.Empty;
    private string _btUuid = string.Empty;

    public bool IsRunning { get; private set; }

    public DiscoveryService(Action<string>? log = null)
    {
        _log = log;
    }

    /// <summary>蓝牙通道信息（自动发现响应中一并返回，供移动端直连）。</summary>
    public void SetBluetooth(bool available, string mac, string uuid)
    {
        _btAvailable = available;
        _btMac = mac ?? string.Empty;
        _btUuid = uuid ?? string.Empty;
    }

    public void Start(int tcpPort)
    {
        if (IsRunning) return;
        _tcpPort = tcpPort;
        try
        {
            _udp = new UdpClient();
            // 允许多网卡 / 重复绑定，避免端口被 TIME_WAIT 占用时启动失败
            _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udp.Client.Bind(new IPEndPoint(IPAddress.Any, AppConstants.DiscoveryPort));
            _cts = new CancellationTokenSource();
            IsRunning = true;
            _ = LoopAsync(_cts.Token);
            _log?.Invoke($"自动发现已开启（UDP {AppConstants.DiscoveryPort}），手机可自动找到本机");
        }
        catch (Exception ex)
        {
            IsRunning = false;
            try { _udp?.Dispose(); } catch { }
            _udp = null;
            _log?.Invoke($"自动发现开启失败（不影响手动填 IP 连接）：{ex.Message}");
        }
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        var udp = _udp;
        while (udp != null && !ct.IsCancellationRequested)
        {
            try
            {
                var recv = await udp.ReceiveAsync(ct);
                var text = Encoding.UTF8.GetString(recv.Buffer).Trim();
                if (!text.Contains(AppConstants.DiscoveryRequest, StringComparison.Ordinal)) continue;

                var reply = BuildReply(recv.RemoteEndPoint.Address);
                var bytes = Encoding.UTF8.GetBytes(reply);
                try
                {
                    await udp.SendAsync(bytes, bytes.Length, recv.RemoteEndPoint);
                    _log?.Invoke($"已响应发现请求：{recv.RemoteEndPoint.Address}");
                }
                catch (Exception ex)
                {
                    _log?.Invoke("响应发现请求失败：" + ex.Message);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { break; }   // 套接字已失效，退出循环而不是空转
            catch (Exception)
            {
                // 未知异常退避，避免死循环刷日志把 UI 拖死
                try { await Task.Delay(500, ct); } catch { break; }
            }
        }
    }

    private string BuildReply(IPAddress remote)
    {
        var best = HotspotHelper.GetBestIpFor(remote) ?? "";
        var ips = new List<string>();
        if (!string.IsNullOrEmpty(best)) ips.Add(best);
        foreach (var ip in HotspotHelper.ListCandidateIps())
            if (!ips.Contains(ip)) ips.Add(ip);

        var sb = new StringBuilder();
        sb.Append("{\"type\":\"yty_here\",\"host\":").Append(JsonEscape(NetworkInfo.HostName))
          .Append(",\"port\":").Append(_tcpPort)
          .Append(",\"version\":").Append(JsonEscape(AppConstants.Version))
          .Append(",\"ips\":[");
        for (int i = 0; i < ips.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append('"').Append(ips[i]).Append('"');
        }
        sb.Append("],\"bt_available\":").Append(_btAvailable ? "true" : "false")
          .Append(",\"bt_mac\":").Append(JsonEscape(_btMac))
          .Append(",\"bt_uuid\":").Append(JsonEscape(_btUuid))
          .Append('}');
        return sb.ToString();
    }

    private static string JsonEscape(string s) =>
        "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    public void Stop()
    {
        if (!IsRunning) return;
        IsRunning = false;
        try { _cts?.Cancel(); } catch { }
        try { _udp?.Dispose(); } catch { }
        _udp = null;
    }

    public void Dispose() => Stop();
}
