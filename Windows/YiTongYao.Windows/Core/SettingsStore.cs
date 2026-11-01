using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace YiTongYao.Core;

/// <summary>
/// 简单的本地设置持久化（JSON 文件）。
/// </summary>
internal sealed class SettingsStore
{
    private static readonly string FilePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "YiTongYao", "settings.json");

    public int Port { get; set; } = AppConstants.DefaultPort;
    public string Token { get; set; } = GenerateToken();

    /// <summary>关闭窗口时最小化到托盘（而不是直接退出）。托盘图标隐藏时此项自动失效。</summary>
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>程序启动后自动开启控制服务。</summary>
    public bool AutoStartServer { get; set; } = true;

    /// <summary>随 Windows 开机自动启动本软件（写入用户级 Run 注册表项）。</summary>
    public bool AutoLaunch { get; set; } = false;

    /// <summary>开机自启时不弹出主窗口，直接进入后台（托盘可见则只驻留托盘）。</summary>
    public bool LaunchSilently { get; set; } = true;

    /// <summary>隐藏系统托盘图标（需配合 Ctrl+Alt+Y 热键唤出主窗口）。</summary>
    public bool HideTrayIcon { get; set; } = false;

    public bool AutoCheckUpdate { get; set; } = true;

    // ---------- 隐私与授权 ----------

    /// <summary>是否已阅读并同意《隐私政策》。未同意时启动会先征求同意。</summary>
    public bool PrivacyAccepted { get; set; } = false;

    /// <summary>同意时间（本机时间），仅用于本机记录，不会上传。</summary>
    public DateTime PrivacyAcceptedAt { get; set; } = DateTime.MinValue;

    // ---------- 安全认证 ----------

    /// <summary>已登记的认证方式（密码 / 系统解锁 / U 盘），各自带保护的操作范围。</summary>
    public List<AuthMethodEntry> AuthMethods { get; set; } = new();

    /// <summary>双因子：需通过两种不同类别的认证方式才算解锁。</summary>
    public bool TwoFactorEnabled { get; set; } = false;

    /// <summary>连续失败次数，达到 5 次触发锁定阶梯。</summary>
    public int PasswordFailedAttempts { get; set; } = 0;

    /// <summary>锁定解除时间（UTC）。在此之前禁止继续尝试。</summary>
    public DateTime PasswordLockoutUntil { get; set; } = DateTime.MinValue;

    /// <summary>当前锁定阶梯层级（决定下一次锁定时长）。</summary>
    public int PasswordLockoutLevel { get; set; } = 0;

    // 旧版单密码字段：仅用于把 1.5.x 的密码迁移到认证方式列表，迁移后不再写入
    public string PasswordHash { get; set; } = "";
    public string SecurityQuestion { get; set; } = "";
    public string SecurityAnswerHash { get; set; } = "";
    public bool RequirePasswordOnOpen { get; set; } = false;

    /// <summary>把旧版单密码升级为一条「密码」认证方式，避免用户已设的密码丢失。</summary>
    private void MigrateLegacy()
    {
        if (AuthMethods.Count > 0) return;
        if (string.IsNullOrEmpty(PasswordHash) && !RequirePasswordOnOpen) return;

        var entry = new AuthMethodEntry
        {
            Kind = AuthMethodKind.Password,
            Name = "主密码",
            Operations = AuthOperation.All.ToList()
        };
        entry.SetPasswordOptions(new AuthPasswordOptions
        {
            PasswordHash = PasswordHash,
            SecurityQuestion = SecurityQuestion,
            SecurityAnswerHash = SecurityAnswerHash
        });
        AuthMethods.Add(entry);

        PasswordHash = "";
        SecurityQuestion = "";
        SecurityAnswerHash = "";
        RequirePasswordOnOpen = false;
    }

    public static SettingsStore Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var s = JsonSerializer.Deserialize<SettingsStore>(json);
                if (s != null)
                {
                    s.MigrateLegacy();
                    return s;
                }
            }
        }
        catch { }
        return new SettingsStore();
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    /// <summary>
    /// 生成一个 6 位随机配对码。
    /// </summary>
    public static string GenerateToken()
    {
        var rnd = new Random();
        return rnd.Next(100000, 999999).ToString();
    }
}
