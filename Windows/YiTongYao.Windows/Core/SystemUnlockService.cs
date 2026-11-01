using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace YiTongYao.Core;

// 系统解锁（Windows Hello / PIN / 指纹 / 凭据）
internal static class SystemUnlockService
{
    private const int ErrorSuccess = 0;
    private const int ErrorCancelled = 1223;
    private const int CreduiwinEnumerateCurrentUser = 0x200;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CreduiInfo
    {
        public int cbSize;
        public IntPtr hwndParent;
        public string pszMessageText;
        public string pszCaptionText;
        public IntPtr hbmBanner;
    }

    [DllImport("credui.dll", CharSet = CharSet.Unicode)]
    private static extern int CredUIPromptForWindowsCredentialsW(
        ref CreduiInfo pUiInfo, int dwAuthError, ref uint pulAuthPackage,
        IntPtr pvInAuthBuffer, uint ulInAuthBufferSize,
        out IntPtr ppvOutAuthBuffer, out uint pulOutAuthBufferSize,
        ref bool pfSave, int dwFlags);

    public static bool IsSupported() => OperatingSystem.IsWindows();

    public static Task<(bool ok, string message)> VerifyAsync(string message)
    {
        return Task.Run(() =>
        {
            try
            {
                if (!OperatingSystem.IsWindows())
                    return (false, "当前平台不支持系统解锁，请换用其它方式。");

                var ui = new CreduiInfo
                {
                    cbSize = Marshal.SizeOf<CreduiInfo>(),
                    pszMessageText = message ?? "易通遥需要验证以继续",
                    pszCaptionText = "易通遥 - 系统解锁"
                };
                uint authPackage = 0;
                bool save = false;
                int r = CredUIPromptForWindowsCredentialsW(
                    ref ui, 0, ref authPackage, IntPtr.Zero, 0,
                    out IntPtr outBuf, out uint outSize, ref save, CreduiwinEnumerateCurrentUser);

                if (outBuf != IntPtr.Zero) Marshal.FreeCoTaskMem(outBuf);

                if (r == ErrorCancelled) return (false, "已取消系统验证。");
                if (r != ErrorSuccess) return (false, "系统验证未通过（错误码 " + r + "）。");
                return (true, "系统验证通过");
            }
            catch (Exception ex)
            {
                return (false, "系统解锁调用失败：" + ex.Message);
            }
        });
    }
}
