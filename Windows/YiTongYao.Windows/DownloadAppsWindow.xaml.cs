using System.Windows;

namespace YiTongYao.Windows;

/// <summary>首次启动提示下载手机端 / 手表端，并提供官方与网盘下载入口。</summary>
public partial class DownloadAppsWindow : Window
{
    public DownloadAppsWindow()
    {
        InitializeComponent();
    }

    private void PhoneBtn_Click(object sender, RoutedEventArgs e) => Core.UpdateService.OpenUrl(Core.AppConstants.GitHubReleasesUrl);
    private void WatchBtn_Click(object sender, RoutedEventArgs e) => Core.UpdateService.OpenUrl(Core.AppConstants.GitHubReleasesUrl);
    private void PanBtn_Click(object sender, RoutedEventArgs e) => Core.UpdateService.OpenUrl(Core.AppConstants.PanDownloadUrl);

    private void StartBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
