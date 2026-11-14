using System.Windows;

namespace YiTongYao.Windows;

// 一次性恢复码展示
public partial class RecoveryCodeWindow : Window
{
    internal bool Skipped { get; private set; }
    private readonly string _code;

    internal RecoveryCodeWindow(string code, bool regenerate = false)
    {
        InitializeComponent();
        _code = code;
        CodeBox.Text = code;
        if (regenerate)
        {
            Title = "易通遥 · 重新生成恢复码";
            SkipBtn.Content = "取消";
        }
        UpdateDone();
    }

    private void UpdateDone() => DoneBtn.IsEnabled = SavedBox.IsChecked == true;

    private void SavedBox_Changed(object sender, RoutedEventArgs e) => UpdateDone();

    private void CopyBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_code);
            CopyHint.Visibility = Visibility.Visible;
        }
        catch { }
    }

    private void DoneBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void SkipBtn_Click(object sender, RoutedEventArgs e)
    {
        Skipped = true;
        DialogResult = false;
        Close();
    }
}
