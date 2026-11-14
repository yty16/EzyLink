using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using YiTongYao.Core;

namespace YiTongYao.Windows;

/// <summary>外观设置：主题模式与强调色，实时预览，保存写设置、取消回滚。</summary>
public partial class AppearanceWindow : Window
{
    // 预设强调色（null = 跟随明暗）
    private static readonly (string Name, string? Hex)[] Presets =
    {
        ("默认", null),
        ("青", "#36E2E8"),
        ("紫", "#965CE9"),
        ("蓝", "#4C8BF5"),
        ("绿", "#3DDC84"),
        ("橙", "#FF9F43"),
        ("粉", "#FF6B9D"),
        ("红", "#FF6B6B"),
    };

    private string? _selectedAccent;
    private readonly string _savedMode;
    private readonly string? _savedAccent;

    public AppearanceWindow()
    {
        InitializeComponent();

        var s = SettingsStore.Load();
        _savedMode = s.ThemeMode;
        _savedAccent = string.IsNullOrWhiteSpace(s.AccentHex) ? null : s.AccentHex;
        _selectedAccent = _savedAccent;

        switch (ThemeManager.ParseMode(s.ThemeMode))
        {
            case ThemeManager.ThemeMode.Light: RbLight.IsChecked = true; break;
            case ThemeManager.ThemeMode.Dark:  RbDark.IsChecked = true; break;
            default:                            RbSystem.IsChecked = true; break;
        }

        BuildSwatches();
    }

    private void BuildSwatches()
    {
        SwatchPanel.Children.Clear();
        foreach (var p in Presets)
        {
            var btn = new Button { Style = (Style)FindResource("Swatch"), ToolTip = p.Name };
            btn.Background = p.Hex == null
                ? new LinearGradientBrush(Colors.Cyan, Colors.Purple, 45)
                : new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(p.Hex)!);
            btn.Tag = p.Hex;
            if (p.Hex == null) btn.Content = "默";
            btn.Click += (_, _) => { _selectedAccent = p.Hex; Highlight(p.Hex); ApplyPreview(); };
            SwatchPanel.Children.Add(btn);
        }
        Highlight(_selectedAccent);
    }

    private void Highlight(string? hex)
    {
        foreach (var child in SwatchPanel.Children)
        {
            if (child is Button b)
                b.BorderBrush = (b.Tag as string) == hex
                    ? new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#ECEAFF")!)
                    : System.Windows.Media.Brushes.Transparent;
        }
    }

    private void Mode_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        ApplyPreview();
    }

    private void ApplyPreview()
    {
        var mode = RbLight.IsChecked == true ? ThemeManager.ThemeMode.Light
                  : RbDark.IsChecked == true ? ThemeManager.ThemeMode.Dark
                  : ThemeManager.ThemeMode.System;
        ThemeManager.ApplyTheme(mode, _selectedAccent);
    }

    private void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        var mode = RbLight.IsChecked == true ? "light"
                  : RbDark.IsChecked == true ? "dark"
                  : "system";
        var s = SettingsStore.Load();
        s.ThemeMode = mode;
        s.AccentHex = _selectedAccent ?? "";
        s.Save();
        DialogResult = true;
        Close();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        // 回滚到进入窗口前的主题
        ThemeManager.ApplyTheme(ThemeManager.ParseMode(_savedMode), _savedAccent);
        DialogResult = false;
        Close();
    }
}
