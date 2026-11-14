using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace YiTongYao.Core;

/// <summary>会话层：把 JSON 指令流转换为动作并回写响应。</summary>
internal static class SessionRunner
{
    /// <summary>底层接收超时（毫秒）。</summary>
    public const int ReadTimeoutMs = 5000;

    private static readonly JsonDocumentOptions DocOptions = default;

    /// <summary>在后台线程跑完一整个会话。</summary>
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
                    // 读超时，继续下一轮。
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

    /// <summary>判断读异常是否为超时。</summary>
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
