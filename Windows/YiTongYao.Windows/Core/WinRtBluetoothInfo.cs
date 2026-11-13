using System.Text;
using Windows.Devices.Bluetooth;

namespace YiTongYao.Core;

/// <summary>本机蓝牙信息（WinRT）</summary>
internal static class WinRtBluetoothInfo
{
    /// <summary>本机蓝牙 MAC；无适配器或查询失败时返回空串。</summary>
    public static async Task<string> LocalMacAsync()
    {
        try
        {
            var adapter = await BluetoothAdapter.GetDefaultAsync();
            return adapter == null ? string.Empty : Format(adapter.BluetoothAddress);
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>地址转换</summary>
    private static string Format(ulong addr)
    {
        if (addr == 0) return string.Empty;
        var b = BitConverter.GetBytes(addr);
        var sb = new StringBuilder(17);
        for (int i = 5; i >= 0; i--)
        {
            if (i < 5) sb.Append(':');
            sb.Append(b[i].ToString("X2"));
        }
        return sb.ToString();
    }
}
