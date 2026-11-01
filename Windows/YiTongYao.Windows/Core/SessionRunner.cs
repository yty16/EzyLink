using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace YiTongYao.Core;

/// <summary>
/// 会话层：把一条「按行分隔的 JSON」指令流转换为具体动作，并回写一行 JSON 响应。
///
/// 与传输方式无关 —— TCP 与其它潜在通道可共用本实现，因此各通道的应用层协议、
/// 鉴权语义、高危指令门禁完全一致，无需为每种通道单独维护一份协议实现。
///
/// 实现说明：内部使用<strong>同步</strong>循环 + 后台 Task，而非 async 读写。
/// 原因：部分通道的套接字由原生句柄提供，无法直接包装为 .NET
/// <see cref="System.Net.Sockets.Socket"/>，只能以自实现的 <see cref="Stream"/> 暴露同步读写。
/// 同步循环同时让「读超时即醒来检查取消」这一逻辑对所有通道统一生效。
/// </summary>
internal static class SessionRunner
{
    /// <summary>
    /// 建议的底层接收超时（毫秒）。
    /// 两条通道都需设置它：TCP 用 <c>TcpClient.ReceiveTimeout</c>，
    /// 蓝牙用 <c>SO_RCVTIMEO</c>；否则阻塞读不会返回，服务停止时会话线程无法退出。
    /// </summary>
    public const int ReadTimeoutMs = 5000;

    private static readonly JsonDocumentOptions DocOptions = default;

    /// <summary>
    /// 在后台线程跑完一整个会话（直到对端断开或取消）。
    /// </summary>
    /// <param name="stream">已建立连接的双向字节流。</param>
    /// <param name="remote">对端可读标识，用于日志。</param>
    /// <param name="token">配对码；为空白时表示不启用鉴权。</param>
    /// <param name="ct">服务停止时用于通知会话退出。</param>
    /// <param name="log">日志回调（已在 UI 线程安全上下文调用，由调用方保证）。</param>
    public static Task RunAsync(Stream stream, string remote, string token,
                                CancellationToken ct, Action<string>? log = null)
    {
        return Task.Run(() => Run(stream, remote, token, ct, log), ct);
    }

    private static void Run(Stream stream, string remote, string token,
                            CancellationToken ct, Action<string>? log)
    {
        bool requireAuth = !string.IsNullOrWhiteSpace(token);
        bool authed = !requireAuth;
        log?.Invoke($"客户端已连接: {remote}");

        try
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, leaveOpen: true);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, leaveOpen: true)
            {
                NewLine = "\n",
                AutoFlush = true
            };

            while (!ct.IsCancellationRequested)
            {
                string? line;
                try
                {
                    line = reader.ReadLine();
                }
                catch (IOException ex) when (IsTimeout(ex))
                {
                    // 读超时（TCP 用 ReceiveTimeout，蓝牙用 SO_RCVTIMEO）：
                    // 回到循环头部检查取消标志，而不是把超时当成致命错误。
                    continue;
                }
                catch (IOException)
                {
                    break;   // 连接真的断了（对端重置 / 适配器关闭），退出会话
                }
                if (line == null) break;   // 对端关闭

                line = line.Trim();
                if (line.Length == 0) continue;

                string cmd = TryGetCmd(line) ?? string.Empty;
                string response;

                if (cmd == "auth")
                {
                    var tok = TryGetToken(line);
                    if (tok == token)
                    {
                        authed = true;
                        response = "{\"ok\":true}";
                        log?.Invoke($"{remote} 鉴权成功");
                    }
                    else
                    {
                        response = "{\"ok\":false,\"err\":\"auth failed\"}";
                        log?.Invoke($"{remote} 鉴权失败");
                    }
                }
                else if (cmd == "ping")
                {
                    // ping 属于探活，不强制鉴权，便于移动端在 auth 前测连通性
                    response = CommandHandler.Handle(line, log);
                }
                else
                {
                    response = requireAuth && !authed
                        ? "{\"ok\":false,\"err\":\"auth required\"}"
                        : CommandHandler.Handle(line, log);
                }

                try
                {
                    writer.WriteLine(response);
                }
                catch (IOException)
                {
                    break;   // 写失败说明连接已断，无需再循环
                }
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"客户端 {remote} 处理异常: {ex.Message}");
        }
        finally
        {
            log?.Invoke($"客户端已断开: {remote}");
        }
    }

    /// <summary>
    /// 判断一次读异常是否只是「超时」。
    /// 超时必须区别于「连接已断」：前者应继续等待下一条指令，
    /// 后者若继续循环会在已失效的套接字上空转刷日志（正是历史版本卡死的成因）。
    /// TCP 与蓝牙两侧都统一包装成带 <see cref="SocketError.TimedOut"/> 的 IOException。
    /// </summary>
    private static bool IsTimeout(IOException ex) =>
        ex.InnerException is SocketException se && se.SocketErrorCode == SocketError.TimedOut;

    private static string? TryGetCmd(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line, DocOptions);
            var r = doc.RootElement;
            if (r.TryGetProperty("cmd", out var c) && c.ValueKind == JsonValueKind.String)
                return c.GetString();
        }
        catch { }
        return null;
    }

    private static string? TryGetToken(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line, DocOptions);
            var r = doc.RootElement;
            if (r.TryGetProperty("token", out var t) && t.ValueKind == JsonValueKind.String)
                return t.GetString();
        }
        catch { }
        return null;
    }
}
