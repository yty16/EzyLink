using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;

namespace YiTongYao.Core;

/// <summary>离线直连辅助：移动热点检测与防火墙放行。</summary>
internal static class HotspotHelper
{
    /// <summary>移动热点是否已开启（存在已启用且带 IPv4 的承载网络 / Wi-Fi Direct 适配器）。</summary>
    public static bool IsHotspotOn()
    {
        try { return FindHotspotAddresses().Count > 0; }
        catch { return false; }
    }

    /// <summary>电脑在移动热点中的地址（手机/手表应连这个地址）。未开启热点时返回 null。</summary>
    public static string? GetHotspotIp()
    {
        try
        {
            var list = FindHotspotAddresses();
            return list.Count > 0 ? list[0] : null;
        }
        catch { return null; }
    }

    /// <summary>热点网卡名称（用于界面提示），未开启返回 null。</summary>
    public static string? GetHotspotName()
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (!IsHotspotInterface(ni)) continue;
                if (GetIpv4(ni).Count == 0) continue;
                return ni.Name;
            }
        }
        catch { }
        return null;
    }

    /// <summary>列出可连接的 IPv4 地址。</summary>
    public static List<string> ListCandidateIps()
    {
        var result = new List<string>();
        try
        {
            var hotspot = FindHotspotAddresses();
            foreach (var ip in hotspot)
                if (!result.Contains(ip)) result.Add(ip);

            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var ip in GetIpv4(ni))
                    if (!result.Contains(ip)) result.Add(ip);
            }
        }
        catch { }
        return result;
    }

    /// <summary>挑选与对端同网段的最佳地址。</summary>
    public static string? GetBestIpFor(IPAddress remote)
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var mask = ua.IPv4Mask;
                    if (mask == null) continue;
                    if (SameSubnet(ua.Address, remote, mask))
                        return ua.Address.ToString();
                }
            }
        }
        catch { }

        return GetHotspotIp() ?? ListCandidateIps().FirstOrDefault();
    }

    /// <summary>打开 Windows「移动热点」系统设置页，引导用户开启热点。</summary>
    public static void OpenHotspotSettings()
    {
        // Windows 11 用 network-mobilehotspot，Windows 10 用 mobilehotspot，两个都试
        foreach (var uri in new[] { "ms-settings:network-mobilehotspot", "ms-settings:mobilehotspot" })
        {
            try
            {
                Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
                return;
            }
            catch { }
        }
    }

    /// <summary>添加防火墙入站放行规则。</summary>
    public static bool TryOpenFirewall(out string message)
    {
        var exe = AutoStart.ExePath;
        var args = "advfirewall firewall add rule name=\"" + FirewallRuleName + "\" dir=in action=allow " +
                   "enable=yes profile=any program=\"" + exe + "\"";
        try
        {
            var psi = new ProcessStartInfo("netsh", args)
            {
                UseShellExecute = true,
                Verb = "runas",   // 请求管理员权限
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var p = Process.Start(psi);
            if (p == null)
            {
                message = "无法启动配置进程";
                return false;
            }
            p.WaitForExit(15000);
            message = p.ExitCode == 0
                ? "已添加防火墙入站放行规则，手机 / 手表现在可以连上本机"
                : "防火墙规则未能添加（退出码 " + p.ExitCode + "）。若已存在同名规则也会返回非 0，通常不影响使用。";
            return p.ExitCode == 0;
        }
        catch (Exception ex)
        {
            // 用户取消 UAC 时也会走到这里
            message = "防火墙配置未执行：" + ex.Message;
            return false;
        }
    }

    public const string FirewallRuleName = "易通遥 远程控制";

    /// <summary>检查防火墙放行规则是否存在。</summary>
    public static bool IsFirewallRulePresent()
    {
        try
        {
            var psi = new ProcessStartInfo("netsh", "advfirewall firewall show rule name=\"" + FirewallRuleName + "\"")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(8000);

            // 按输出规模判断规则是否存在。
            return p.ExitCode == 0 && output.Trim().Length > 50;
        }
        catch
        {
            // 查询失败时按「已放行」处理，避免误报打扰
            return true;
        }
    }

    // ---------- 内部实现 ----------

    private static bool IsHotspotInterface(NetworkInterface ni)
    {
        var desc = ni.Description ?? "";
        var name = ni.Name ?? "";
        return desc.Contains("Wi-Fi Direct", StringComparison.OrdinalIgnoreCase)
               || desc.Contains("Hosted Network", StringComparison.OrdinalIgnoreCase)
               || desc.Contains("Microsoft Wi-Fi Direct", StringComparison.OrdinalIgnoreCase)
               || name.StartsWith("本地连接*")
               || name.StartsWith("Local Area Connection*")
               || name.Contains("热点")
               || name.Contains("Hotspot");
    }

    private static List<string> FindHotspotAddresses()
    {
        var list = new List<string>();
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (!IsHotspotInterface(ni)) continue;
            foreach (var ip in GetIpv4(ni))
                if (!list.Contains(ip)) list.Add(ip);
        }
        return list;
    }

    private static List<string> GetIpv4(NetworkInterface ni)
    {
        var list = new List<string>();
        foreach (var ua in ni.GetIPProperties().UnicastAddresses)
        {
            if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
            var ip = ua.Address;
            if (IPAddress.IsLoopback(ip)) continue;
            var b = ip.GetAddressBytes();
            if (b[0] == 169 && b[1] == 254) continue; // APIPA 自动私有地址，不可用
            list.Add(ip.ToString());
        }
        return list;
    }

    private static bool SameSubnet(IPAddress a, IPAddress b, IPAddress mask)
    {
        var ab = a.GetAddressBytes();
        var bb = b.GetAddressBytes();
        var mb = mask.GetAddressBytes();
        if (ab.Length != 4 || bb.Length != 4 || mb.Length != 4) return false;
        for (int i = 0; i < 4; i++)
            if ((ab[i] & mb[i]) != (bb[i] & mb[i])) return false;
        return true;
    }
}
