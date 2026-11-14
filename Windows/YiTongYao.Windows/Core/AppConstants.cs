namespace YiTongYao.Core;

/// <summary>
/// 易通遥 全局常量。ProductCode 为内部构建标识（Base64），不影响功能。
/// </summary>
internal static class AppConstants
{
    public const string AppName = "易通遥";
    public const string AppNameEn = "EzyLink";
    public const int DefaultPort = 7788;

    /// <summary>UDP 广播自动发现端口：手机/手表无需手填 IP 即可找到电脑。</summary>
    public const int DiscoveryPort = 7789;

    /// <summary>自动发现请求/响应标记。</summary>
    public const string DiscoveryRequest = "YTY_DISCOVER";

    // ---- 自动更新（GitHub Releases）----
    public const string GitHubOwner = "yty16";
    public const string GitHubRepo = "EzyLink";
    public const string GitHubReleasesUrl = "https://github.com/yty16/EzyLink/releases";
    public const string GitHubLatestApi = "https://api.github.com/repos/yty16/EzyLink/releases/latest";

    // 内部产品标识（Base64），仅用于构建/分发溯源，不做任何鉴权用途。
    internal const string ProductCode = "eXR5MTY=";

    public const string Version = "1.9.1.5";
}
