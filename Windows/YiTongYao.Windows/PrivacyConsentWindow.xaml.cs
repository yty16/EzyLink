using System;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace YiTongYao.Windows;

/// <summary>隐私告知与同意窗口。</summary>
public partial class PrivacyConsentWindow : Window
{
    public bool Accepted { get; private set; }

    private readonly bool _required;
    private bool _decided;

    public PrivacyConsentWindow(bool required)
    {
        _required = required;
        InitializeComponent();

        SubtitleText.Text = required
            ? "本软件首次启动需要你确认以下内容后方可继续使用。全部数据仅在本机与你的局域网内处理，不上传网络。"
            : "你可随时重新查看并调整授权。全部数据仅在本机与你的局域网内处理，不上传网络。";

        DeclineBtn.Content = required ? "不同意，退出程序" : "关闭";
        RefreshButtons();
    }

    private void RefreshButtons()
    {
        bool policy = PolicyCheck.IsChecked == true;
        bool terms = TermsCheck.IsChecked == true;
        AcceptBtn.IsEnabled = policy && terms;
        HintText.Text = (policy && terms)
            ? "点击「同意并继续」后即可使用本软件；你随时可在主界面「隐私与授权」区撤回同意。"
            : "请先勾选「我已阅读并同意《隐私政策》」与「我已阅读并同意《免责声明》与《用户协议》」后，方可点击「同意并继续」。";
    }

    private void PolicyCheck_Changed(object sender, RoutedEventArgs e) => RefreshButtons();

    private void TermsCheck_Changed(object sender, RoutedEventArgs e) => RefreshButtons();

    private void AcceptBtn_Click(object sender, RoutedEventArgs e)
    {
        if (PolicyCheck.IsChecked != true) return;
        Accepted = true;
        _decided = true;
        Close();
    }

    private void DeclineBtn_Click(object sender, RoutedEventArgs e)
    {
        _decided = true;
        Close();
    }

    private void RepoLink_Click(object sender, RoutedEventArgs e)
    {
        // 优先打开随程序发布的 PRIVACY.md；不存在时回退到仓库地址
        if (!TryOpenLocalPrivacy())
            OpenUrl("https://github.com/" + Core.AppConstants.GitHubOwner + "/" + Core.AppConstants.GitHubRepo);
    }

    private void IssueLink_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl("https://github.com/" + Core.AppConstants.GitHubOwner + "/" + Core.AppConstants.GitHubRepo + "/issues");
    }

    internal static bool TryOpenLocalPrivacy()
    {
        try
        {
            string dir = AppContext.BaseDirectory;
            string path = Path.Combine(dir, "PRIVACY.md");
            if (!File.Exists(path)) return false;
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { }
    }

    /// <summary>强制模式下不允许用关闭按钮绕过，必须明确表态。</summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_decided && _required)
        {
            e.Cancel = true;
            return;
        }
        base.OnClosing(e);
    }
}
