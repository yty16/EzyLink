using System.Windows;
using YiTongYao.Core;

namespace YiTongYao.Windows;

public partial class UpdateWindow : Window
{
    private readonly UpdateInfo _info;
    private bool _updating;

    public UpdateWindow(UpdateInfo info)
    {
        InitializeComponent();
        _info = info;

        FromText.Text = "当前版本 v" + UpdateService.CurrentVersion;
        ToText.Text = "v" + info.Version;
        ChangelogText.Text = string.IsNullOrWhiteSpace(info.Changelog)
            ? "（本次更新未提供更新说明，可打开发布页查看详细内容。）"
            : info.Changelog.Trim();
    }

    private void LaterBtn_Click(object sender, RoutedEventArgs e) => Close();

    private void PageBtn_Click(object sender, RoutedEventArgs e)
    {
        UpdateService.OpenUrl(_info.ReleaseUrl);
    }

    private async void UpdateBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        _updating = true;
        UpdateBtn.IsEnabled = false;
        LaterBtn.IsEnabled = false;

        void Progress(string msg) => Dispatcher.Invoke(() => ProgressText.Text = msg);

        var started = await UpdateService.PerformUpdateAsync(_info, Progress);

        if (started)
        {
            // 安装程序已启动：关闭本程序，让安装器覆盖更新文件
            try { System.Windows.Application.Current.Shutdown(); }
            catch { Environment.Exit(0); }
            return;
        }

        Progress("未能启动安装程序，已打开发布页，请手动下载安装。");
        UpdateBtn.IsEnabled = true;
        LaterBtn.IsEnabled = true;
        _updating = false;
    }
}
