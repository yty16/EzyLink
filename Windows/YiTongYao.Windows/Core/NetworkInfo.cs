using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace YiTongYao.Core;

/// <summary>获取本机网络信息。</summary>
internal static class NetworkInfo
{
    public static string HostName => Dns.GetHostName();

    /// <summary>选择合适的本机 IPv4 地址。</summary>
    public static string? GetLocalIp()
    {
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            if (ni.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase)) continue;
            if (ni.Description.Contains("Pseudo", StringComparison.OrdinalIgnoreCase)) continue;

            foreach (var ua in ni.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                var ip = ua.Address;
                if (IPAddress.IsLoopback(ip)) continue;
                if (ip.GetAddressBytes()[0] == 169 && ip.GetAddressBytes()[1] == 254) continue; // APIPA
                return ip.ToString();
            }
        }
        // 兜底
        var host = Dns.GetHostEntry(Dns.GetHostName());
        foreach (var ip in host.AddressList)
            if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                return ip.ToString();
        return null;
    }

    /// <summary>返回对应的 MAC 地址。</summary>
    public static string? GetMac()
    {
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            if (ni.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase)) continue;
            if (ni.Description.Contains("Pseudo", StringComparison.OrdinalIgnoreCase)) continue;

            var addrs = ni.GetIPProperties().UnicastAddresses;
            bool hasIpv4 = false;
            foreach (var ua in addrs)
                if (ua.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(ua.Address) &&
                    !(ua.Address.GetAddressBytes()[0] == 169 && ua.Address.GetAddressBytes()[1] == 254))
                    hasIpv4 = true;
            if (!hasIpv4) continue;

            var mac = ni.GetPhysicalAddress();
            if (mac != null && mac.GetAddressBytes().Length == 6)
                return string.Join("-", mac.GetAddressBytes().Select(b => b.ToString("XX")));
        }
        return null;
    }
}
