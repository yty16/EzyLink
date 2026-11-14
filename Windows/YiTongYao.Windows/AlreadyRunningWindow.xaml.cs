using System.Windows;

namespace YiTongYao.Windows;

/// <summary>
/// 重复启动提示：易通遥 已在运行。
/// DialogResult：true = 重启当前实例；false/null = 取消（保持已有实例）。
/// </summary>
public partial class AlreadyRunningWindow : Window
{
    public AlreadyRunningWindow()
    {
        InitializeComponent();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void RestartLink_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
