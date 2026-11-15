using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;

namespace YiTongYao.Core;

/// <summary>更新信息。</summary>
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

/// <summary>自动更新服务。</summary>
internal static class UpdateService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(25) };
    private static readonly HttpClient _dl = new() { Timeout = Timeout.InfiniteTimeSpan };

    static UpdateService()
    {
        try
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("YiTongYao-Updater");
            _dl.DefaultRequestHeaders.UserAgent.ParseAdd("YiTongYao-Updater");
        }
        catch { }
    }

    // GitHub 代理镜像（备用下载源），随时可能变更或失效。
    private static readonly string[] ProxyHosts =
    {
        "https://gh-proxy.com/",
        "https://ghfast.top/",
    };

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

    /// <summary>下载并启动安装包。</summary>
    public static async Task<bool> PerformUpdateAsync(UpdateInfo info, Action<string>? progress = null, bool silent = false)
    {
        var asset = PickAsset(info.Assets);
        if (asset == null)
        {
            progress?.Invoke("未找到适用于本机的安装包");
            if (!silent) OpenUrl(info.ReleaseUrl);
            return false;
        }

        var tmp = Path.Combine(Path.GetTempPath(), asset.Name);
        foreach (var url in BuildCandidateUrls(asset))
        {
            var src = SourceLabel(url);
            progress?.Invoke("正在从" + src + "下载 " + asset.Name + "（" + asset.SizeText + "）…");
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }

            if (await TryDownloadAsync(url, tmp, asset.Size, progress, src))
            {
                progress?.Invoke("下载完成，正在启动安装程序…");
                return LaunchInstaller(tmp, info.ReleaseUrl, silent);
            }
            progress?.Invoke(src + "下载失败，尝试下一源…");
        }

        progress?.Invoke("所有下载源均失败，已打开发布页");
        if (!silent) OpenUrl(info.ReleaseUrl);
        return false;
    }

    // 构建下载候选源：官方源优先，代理镜像兜底。
    private static List<string> BuildCandidateUrls(UpdateAsset asset)
    {
        var urls = new List<string> { asset.Url };
        foreach (var p in ProxyHosts)
            urls.Add(p + asset.Url);
        return urls;
    }

    private static string SourceLabel(string url)
    {
        foreach (var p in ProxyHosts)
        {
            var host = p.Replace("https://", "").TrimEnd('/');
            if (url.Contains(host)) return host + " 镜像";
        }
        return "GitHub 官方源";
    }

    // 单源下载：6 分钟上限，流式写入并校验大小。
    private static async Task<bool> TryDownloadAsync(string url, string tmp, long expectSize, Action<string>? progress, string src)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(6));
        try
        {
            using var resp = await _dl.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            if (!resp.IsSuccessStatusCode) return false;
            var total = resp.Content.Headers.ContentLength ?? 0;
            await using var fs = File.Create(tmp);
            await using var stream = await resp.Content.ReadAsStreamAsync(cts.Token);
            var buf = new byte[81920];
            long read = 0;
            int n;
            while ((n = await stream.ReadAsync(buf, cts.Token)) > 0)
            {
                await fs.WriteAsync(buf.AsMemory(0, n), cts.Token);
                read += n;
                if (total > 0 && (read & 0x3FFFF) == 0)
                    progress?.Invoke("从" + src + "下载中 " + (read / 1048576.0).ToString("0.0") + " / " + (total / 1048576.0).ToString("0.0") + " MB");
            }
            await fs.FlushAsync(cts.Token);

            var len = new FileInfo(tmp).Length;
            return expectSize > 0 ? len == expectSize : len >= 1024 * 1024;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>选取本机平台的安装包。</summary>
    private static UpdateAsset? PickAsset(List<UpdateAsset> assets)
    {
        return assets.FirstOrDefault(a =>
                   a.Name.Contains("win-x64", StringComparison.OrdinalIgnoreCase) &&
                   a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
               ?? assets.FirstOrDefault(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
    }

    private static bool LaunchInstaller(string path, string fallbackUrl, bool silent)
    {
        try
        {
            var psi = new ProcessStartInfo(path) { UseShellExecute = true };
            if (silent) psi.ArgumentList.Add("/S");
            Process.Start(psi);
            return true;
        }
        catch
        {
            if (!silent) OpenUrl(fallbackUrl);
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

    /// <summary>四段版本比较。</summary>
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
