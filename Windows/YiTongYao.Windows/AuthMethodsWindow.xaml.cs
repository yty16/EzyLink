using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using YiTongYao.Core;

namespace YiTongYao.Windows;

// 认证方式管理
public partial class AuthMethodsWindow : Window
{
    private readonly SettingsStore _s;
    private readonly List<CheckBox> _opsChecks = new();
    private bool _loading;

    internal AuthMethodsWindow(SettingsStore s)
    {
        InitializeComponent();
        _s = s;
        AuthService.Settings = s;
        BuildOpsChecks();
        RefreshList();
        _loading = true;
        TwoFactorBox.IsChecked = _s.TwoFactorEnabled;
        _loading = false;
    }

    private void BuildOpsChecks()
    {
        _opsChecks.Clear();
        OpsPanel.Children.Clear();
        foreach (var op in AuthOperation.All)
        {
            var cb = new CheckBox
            {
                Content = AuthOperation.Label(op),
                Tag = op,
                FontSize = 12,
                Margin = new Thickness(0, 2, 14, 2)
            };
            cb.SetResourceReference(Control.ForegroundProperty, "AppFg");
            cb.Checked += Op_Changed;
            cb.Unchecked += Op_Changed;
            _opsChecks.Add(cb);
            OpsPanel.Children.Add(cb);
        }
    }

    private void RefreshList()
    {
        MethodList.ItemsSource = null;
        MethodList.ItemsSource = _s.AuthMethods;
        if (_s.AuthMethods.Count > 0 && MethodList.SelectedIndex < 0) MethodList.SelectedIndex = 0;
        RefreshOps();
    }

    private AuthMethodEntry? Selected() => MethodList.SelectedItem as AuthMethodEntry;

    private void RefreshOps()
    {
        var m = Selected();
        OpsTitle.Text = m == null ? "保护的操作（先选择一种认证方式）"
                                  : "保护的操作 —— " + AuthMethodKinds.Label(m.Kind);
        _loading = true;
        foreach (var cb in _opsChecks)
            cb.IsChecked = m != null && m.Operations.Contains((string)cb.Tag);
        _loading = false;
        bool has = m != null;
        foreach (var cb in _opsChecks) cb.IsEnabled = has;
        EditBtn.IsEnabled = has && m!.Kind == AuthMethodKind.Password;
        RemoveBtn.IsEnabled = has;
    }

    private void MethodList_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshOps();

    private void Op_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var m = Selected();
        if (m == null) return;
        m.Operations = _opsChecks.Where(c => c.IsChecked == true)
                                 .Select(c => (string)c.Tag).ToList();
        _s.Save();
        var idx = MethodList.SelectedIndex;
        RefreshList();
        MethodList.SelectedIndex = idx;
    }

    private void TwoFactor_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _s.TwoFactorEnabled = TwoFactorBox.IsChecked == true;
        _s.Save();
    }

    private void AddPwdBtn_Click(object sender, RoutedEventArgs e)
    {
        var win = new SetPasswordWindow(_s, null) { Owner = this };
        if (win.ShowDialog() == true) { RefreshList(); MethodList.SelectedIndex = _s.AuthMethods.Count - 1; }
    }

    private void AddSystemBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!SystemUnlockService.IsSupported())
        {
            System.Windows.MessageBox.Show(this, "当前平台不支持系统解锁。", "易通遥",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _s.AuthMethods.Add(new AuthMethodEntry
        {
            Kind = AuthMethodKind.System,
            Name = "Windows Hello",
            Operations = AuthOperation.All.ToList()
        });
        _s.Save();
        RefreshList();
        MethodList.SelectedIndex = _s.AuthMethods.Count - 1;
    }

    private void AddUsbBtn_Click(object sender, RoutedEventArgs e)
    {
        var pick = PickUsbVolume();
        if (pick == null) return;
        var (root, fileMode) = pick.Value;

        var opts = new AuthUsbOptions { UseFileMode = fileMode };
        if (fileMode)
        {
            string token = UsbUnlockService.GenerateToken();
            if (!UsbUnlockService.WriteToken(root, token))
            {
                System.Windows.MessageBox.Show(this, "无法写入 U 盘令牌文件，请检查 U 盘是否可写。",
                    "易通遥", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            opts.TokenHash = UsbUnlockService.HashToken(token);
        }
        else
        {
            var serial = UsbUnlockService.GetVolumeSerial(root);
            if (serial == null)
            {
                System.Windows.MessageBox.Show(this, "无法读取该 U 盘的卷序列号。",
                    "易通遥", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            opts.SerialHash = UsbUnlockService.HashToken(serial);
        }
        opts.DriveLabel = root;

        _s.AuthMethods.Add(new AuthMethodEntry
        {
            Kind = AuthMethodKind.Usb,
            Name = "U 盘 " + root.TrimEnd('\\'),
            Operations = AuthOperation.All.ToList()
        });
        _s.AuthMethods[_s.AuthMethods.Count - 1].SetUsbOptions(opts);
        _s.Save();
        RefreshList();
        MethodList.SelectedIndex = _s.AuthMethods.Count - 1;
    }

    // U 盘选择
    private (string root, bool fileMode)? PickUsbVolume()
    {
        var vols = UsbUnlockService.ListCandidateVolumes();
        if (vols.Count == 0)
        {
            System.Windows.MessageBox.Show(this, "未检测到可移动磁盘，请插入 U 盘后重试。", "易通遥",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }

        var dlg = new Window
        {
            Title = "选择要登记的 U 盘",
            Width = 420,
            Height = 300,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        dlg.SetResourceReference(Control.BackgroundProperty, "AppBg");
        dlg.SetResourceReference(Control.ForegroundProperty, "AppFg");
        var root = new StackPanel { Margin = new Thickness(14) };
        var hint = new TextBlock
        {
            Text = "检测到以下可移动磁盘：",
            Margin = new Thickness(0, 0, 0, 8)
        };
        hint.SetResourceReference(Control.ForegroundProperty, "AppFg");
        root.Children.Add(hint);
        var lb = new ListBox { Height = 120 };
        lb.SetResourceReference(Control.BackgroundProperty, "AppSurface");
        lb.SetResourceReference(Control.ForegroundProperty, "AppFg");
        lb.ItemsSource = vols.Select(v => v.Label + "   (" + v.RootPath + ")").ToList();
        lb.SelectedIndex = 0;
        root.Children.Add(lb);
        var chk = new CheckBox
        {
            Content = "令牌文件模式（在 U 盘写入隐藏令牌文件，校验更严格）",
            Margin = new Thickness(0, 10, 0, 0)
        };
        chk.SetResourceReference(Control.ForegroundProperty, "AppFg");
        root.Children.Add(chk);

        (string, bool)? result = null;
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var ok = new Button { Content = "确定", Width = 80, Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 0, 8, 0) };
        ok.SetResourceReference(Control.BackgroundProperty, "AppControlBg");
        ok.SetResourceReference(Control.ForegroundProperty, "AppFg");
        ok.Click += (_, _) =>
        {
            if (lb.SelectedIndex < 0) return;
            result = (vols[lb.SelectedIndex].RootPath, chk.IsChecked == true);
            dlg.DialogResult = true;
            dlg.Close();
        };
        var cancel = new Button { Content = "取消", Width = 80, Padding = new Thickness(8, 5, 8, 5) };
        cancel.SetResourceReference(Control.BackgroundProperty, "AppControlBg");
        cancel.SetResourceReference(Control.ForegroundProperty, "AppFg");
        cancel.Click += (_, _) => { dlg.DialogResult = false; dlg.Close(); };
        row.Children.Add(ok);
        row.Children.Add(cancel);
        root.Children.Add(row);
        dlg.Content = root;

        return dlg.ShowDialog() == true ? result : null;
    }

    private void EditBtn_Click(object sender, RoutedEventArgs e)
    {
        var m = Selected();
        if (m == null || m.Kind != AuthMethodKind.Password) return;
        var win = new SetPasswordWindow(_s, m) { Owner = this };
        if (win.ShowDialog() == true) RefreshList();
    }

    private void RemoveBtn_Click(object sender, RoutedEventArgs e)
    {
        var m = Selected();
        if (m == null) return;
        var r = System.Windows.MessageBox.Show(this, "确定删除该认证方式吗？", "易通遥 安全认证",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;
        _s.AuthMethods.Remove(m);
        _s.Save();
        RefreshList();
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
