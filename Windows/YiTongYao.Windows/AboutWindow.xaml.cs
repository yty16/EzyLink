using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Documents;

namespace YiTongYao.Windows;

/// <summary>
/// 关于本应用：展示应用名称、版本、简介、开源仓库、开发者、隐私政策与许可证。
/// 仓库 / 开发者 / 协议链接在默认浏览器中打开；本地 PRIVACY.md 优先。
/// </summary>
public partial class AboutWindow : Window
{
    private const string RepoUrl = "https://github.com/yty16/EzyLink";
    private const string DevUrl = "https://github.com/yty16";
    private const string TermsUrl = "https://github.com/yty16/EzyLink/blob/main/TERMS.md";
    private const string DisclaimerUrl = "https://github.com/yty16/EzyLink/blob/main/DISCLAIMER.md";

    public AboutWindow()
    {
        InitializeComponent();
        try
        {
            VersionText.Text = "版本 " + Core.AppConstants.Version;
        }
        catch { }
    }

    private void RepoLink_Click(object sender, RoutedEventArgs e) => OpenUrl(RepoUrl);
    private void DevLink_Click(object sender, RoutedEventArgs e) => OpenUrl(DevUrl);
    private void TermsLink_Click(object sender, RoutedEventArgs e) => OpenUrl(TermsUrl);
    private void DisclaimerLink_Click(object sender, RoutedEventArgs e) => OpenUrl(DisclaimerUrl);

    private void PrivacyLink_Click(object sender, RoutedEventArgs e)
    {
        if (!TryOpenLocal("PRIVACY.md"))
            OpenUrl(RepoUrl + "/blob/main/PRIVACY.md");
    }

    private void LicenseLink_Click(object sender, RoutedEventArgs e)
    {
        if (!TryOpenLocal("LICENSE"))
            OpenUrl(RepoUrl + "/blob/main/LICENSE");
    }

    private static bool TryOpenLocal(string fileName)
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, fileName);
            if (!File.Exists(path)) return false;
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        }
        catch { return false; }
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { }
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();
}
