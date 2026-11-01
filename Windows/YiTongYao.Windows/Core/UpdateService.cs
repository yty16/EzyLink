using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;

namespace YiTongYao.Core;

/// <summary>
/// 更新信息（对标 PeekShield UpdateService：GitHub Releases latest 接口）。
/// </summary>
public class UpdateInfo
{
    public string? Version { get; set; }
    public string? TagName { get; set; }
    public string? Name { get; set; }
    public string Changelog { get; set; } = "";
    public string ReleaseUrl { get; set; } = AppConstants.GitHubReleasesUrl;
    public List<UpdateAsset> Assets { get; set; } = new();
    public bool HasUpdate { get; set; }
    public string? Error { get; set; }
}

public class UpdateAsset
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public long Size { get; set; }

    public string SizeText => Size <= 0 ? "" : (Size / 1024.0 / 1024.0).ToString("0.0") + " MB";
}

/// <summary>
/// 自动更新服务：检查 GitHub Releases 最新版本 → 版本比较 → 下载 win-x64 安装包 → 启动安装器并退出本程序。
/// 全程可失败降级：网络异常时提示用户到发布页手动下载，不影响主功能使用。
/// </summary>
internal static class UpdateService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(25) };

    static UpdateService()
    {
        try { _http.DefaultRequestHeaders.UserAgent.ParseAdd("YiTongYao-Updater"); } catch { }
    }

    public static string CurrentVersion
    {
        get
        {
            if (System.Version.TryParse(AppConstants.Version, out var v))
                return v.ToString();
            return AppConstants.Version;
        }
    }

    public static async Task<UpdateInfo> CheckAsync()
    {
        var info = new UpdateInfo();
        try
        {
            using var resp = await _http.GetAsync(AppConstants.GitHubLatestApi);
            if (!resp.IsSuccessStatusCode)
            {
                info.Error = (int)resp.StatusCode == 404
                    ? "发布页暂无 Release（尚未发布可更新的版本）"
                    : "无法连接更新服务器（HTTP " + (int)resp.StatusCode + "）";
                return info;
            }

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("tag_name", out var t)) info.TagName = t.GetString();
            if (root.TryGetProperty("name", out var n)) info.Name = n.GetString();
            if (root.TryGetProperty("body", out var b)) info.Changelog = b.GetString() ?? "";
            if (root.TryGetProperty("html_url", out var h)) info.ReleaseUrl = h.GetString() ?? info.ReleaseUrl;

            if (root.TryGetProperty("assets", out var a) && a.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in a.EnumerateArray())
                {
                    var name = item.TryGetProperty("name", out var an) ? (an.GetString() ?? "") : "";
                    var url = item.TryGetProperty("browser_download_url", out var au) ? (au.GetString() ?? "") : "";
                    long size = item.TryGetProperty("size", out var sz) && sz.TryGetInt64(out var sv) ? sv : 0;
                    if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(url))
                        info.Assets.Add(new UpdateAsset { Name = name, Url = url, Size = size });
                }
            }

            info.Version = NormalizeVersion(info.TagName ?? info.Name);
            info.HasUpdate = IsNewer(info.Version);
        }
        catch (Exception ex)
        {
            info.Error = "检查更新失败：" + ex.Message;
        }
        return info;
    }

    /// <summary>
    /// 下载并启动安装包；返回是否成功启动安装器。
    /// </summary>
    public static async Task<bool> PerformUpdateAsync(UpdateInfo info, Action<string>? progress = null)
    {
        try
        {
            var asset = PickAsset(info.Assets);
            if (asset == null)
            {
                OpenUrl(info.ReleaseUrl);
                return false;
            }

            progress?.Invoke("正在下载 " + asset.Name + "…");
            var tmp = Path.Combine(Path.GetTempPath(), asset.Name);
            using var resp = await _http.GetAsync(asset.Url);
            resp.EnsureSuccessStatusCode();
            await using (var fs = File.Create(tmp))
                await resp.Content.ReadAsStream().CopyToAsync(fs);

            progress?.Invoke("下载完成，正在启动安装程序…");
            return LaunchInstaller(tmp, info.ReleaseUrl);
        }
        catch (Exception ex)
        {
            progress?.Invoke("自动更新失败：" + ex.Message);
            OpenUrl(info.ReleaseUrl);
            return false;
        }
    }

    /// <summary>
    /// 选取本机平台的安装包：Windows 取 win-x64 + .exe。
    /// </summary>
    private static UpdateAsset? PickAsset(List<UpdateAsset> assets)
    {
        return assets.FirstOrDefault(a =>
                   a.Name.Contains("win-x64", StringComparison.OrdinalIgnoreCase) &&
                   a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
               ?? assets.FirstOrDefault(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
    }

    private static bool LaunchInstaller(string path, string fallbackUrl)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        }
        catch
        {
            OpenUrl(fallbackUrl);
            return false;
        }
    }

    public static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { }
    }

    private static string? NormalizeVersion(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim().TrimStart('v', 'V');
        return s.Contains('.') ? s : null;
    }

    /// <summary>
    /// a.b.c.d 四段版本比较（.NET Version 支持 4 段）。
    /// </summary>
    private static bool IsNewer(string? remote)
    {
        if (string.IsNullOrWhiteSpace(remote)) return false;
        try
        {
            var cur = new Version(CurrentVersion);
            var rem = new Version(remote);
            return rem > cur;
        }
        catch { return false; }
    }
}
