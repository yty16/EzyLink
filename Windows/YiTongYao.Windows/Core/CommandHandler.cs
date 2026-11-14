using System.Text.Json;

namespace YiTongYao.Core;

/// <summary>指令分发：把 JSON 指令转换为动作并返回响应。</summary>
internal static class CommandHandler
{
    public static string Handle(string line, Action<string>? log = null)
    {
        string? cmd = null;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("cmd", out var cmdEl) ||
                cmdEl.ValueKind != JsonValueKind.String)
            {
                return Err("missing or invalid cmd field");
            }
            cmd = cmdEl.GetString();
            if (string.IsNullOrWhiteSpace(cmd)) return Err("empty cmd");

            switch (cmd.ToLowerInvariant())
            {
                case "ping":
                    log?.Invoke("收到 ping");
                    return "{\"ok\":true,\"t\":" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + "}";

                case "info":
                    var ip = NetworkInfo.GetLocalIp();
                    var mac = NetworkInfo.GetMac();
                    var host = NetworkInfo.HostName;
                    log?.Invoke("收到 info 请求");
                    return "{\"ok\":true,\"ip\":" + J(ip) + ",\"mac\":" + J(mac) + ",\"hostname\":" + J(host) + "}";

                case "ppt_start": PptController.Start(); log?.Invoke("PPT 开始放映"); return Ok();
                case "ppt_begin": PptController.Begin(); log?.Invoke("PPT 从头放映"); return Ok();
                case "ppt_next": PptController.Next(); log?.Invoke("PPT 下一张"); return Ok();
                case "ppt_prev": PptController.Prev(); log?.Invoke("PPT 上一张"); return Ok();
                case "ppt_exit": PptController.Exit(); log?.Invoke("PPT 退出放映"); return Ok();
                case "ppt_black": PptController.Black(); log?.Invoke("PPT 黑屏切换"); return Ok();
                case "ppt_white": PptController.White(); log?.Invoke("PPT 白屏切换"); return Ok();

                case "lock": SystemController.Lock(); log?.Invoke("锁定工作站"); return Ok();
                case "sleep": SystemController.Sleep(); log?.Invoke("系统睡眠"); return Ok();
                case "hibernate": SystemController.Hibernate(); log?.Invoke("系统休眠"); return Ok();
                case "shutdown": SystemController.Shutdown(); log?.Invoke("系统关机"); return Ok();
                case "restart": SystemController.Restart(); log?.Invoke("系统重启"); return Ok();

                default:
                    return Err("unknown cmd: " + cmd);
            }
        }
        catch (JsonException)
        {
            return Err("invalid json");
        }
        catch (Exception ex)
        {
            log?.Invoke("执行失败: " + ex.Message);
            return Err("internal error: " + ex.Message);
        }
    }

    private static string Ok() => "{\"ok\":true}";

    private static string Err(string msg) => "{\"ok\":false,\"err\":" + J(msg) + "}";

    private static string J(string? s) => JsonSerializer.Serialize(s);
}
