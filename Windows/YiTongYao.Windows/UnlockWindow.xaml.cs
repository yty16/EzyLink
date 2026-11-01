using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using YiTongYao.Core;

namespace YiTongYao.Windows;

// 解锁窗口（密码 / 系统解锁 / U 盘，支持双因子与保密问题找回）
public partial class UnlockWindow : Window
{
    private readonly SettingsStore _s;
    private readonly List<AuthMethodEntry> _methods;
    private readonly DispatcherTimer _lockTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    private readonly HashSet<AuthMethodKind> _passedKinds = new();
    private readonly HashSet<string> _passedCategories = new();
    private bool _requireTwo;
    private bool _busy;

    private PasswordBox? _pwdBox;
    private PasswordBox? _answerBox;

    /// <summary>通过保密问题找回后，调用方应引导用户重新设置密码。</summary>
    public bool WantsPasswordReset { get; private set; }

    private AuthMethodEntry? Selected() => MethodList.SelectedItem as AuthMethodEntry;

    internal UnlockWindow(SettingsStore s, string operation)
    {
        InitializeComponent();
        _s = s;
        AuthService.Settings = s;
        _methods = AuthService.MethodsFor(s, operation);
        _requireTwo = AuthService.RequireTwoFactors(s, _methods);

        MethodList.ItemsSource = _methods;
        if (_methods.Count > 0) MethodList.SelectedIndex = 0;

        PromptText.Text = _requireTwo
            ? "已启用双因子验证：请先用一种方式验证，再换一种不同类别的方式完成解锁。"
            : "请选择一种方式完成验证。";

        Loaded += (_, _) => { RenderContent(); RefreshLockoutUi(); };
        Closed += (_, _) => _lockTimer.Stop();
        _lockTimer.Tick += (_, _) => UpdateLockCountdown();
        RecoverText.PreviewMouseDown += (_, _) => RenderRecovery();
    }

    private void MethodList_SelectionChanged(object sender, SelectionChangedEventArgs e) => RenderContent();

    private void RenderContent()
    {
        ContentHost.Children.Clear();
        _pwdBox = null;
        _answerBox = null;
        var m = Selected();
        if (m == null) return;

        bool hasQuestion = m.Kind == AuthMethodKind.Password
                           && !string.IsNullOrEmpty(m.GetPasswordOptions().SecurityQuestion);
        RecoverText.Visibility = hasQuestion ? Visibility.Visible : Visibility.Collapsed;
        if (hasQuestion && HintText.Visibility == Visibility.Visible && !IsLockHint())
            HintText.Visibility = Visibility.Collapsed;

        if (m.Kind == AuthMethodKind.Password)
        {
            _pwdBox = new PasswordBox
            {
                Background = System.Windows.Media.Brushes.Transparent,
                Foreground = System.Windows.Media.Brushes.White,
                BorderBrush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x3a, 0x2a, 0x6e)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 8)
            };
            _pwdBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) TryPassword(m); };
            ContentHost.Children.Add(_pwdBox);

            var ok = new Button
            {
                Content = "确定",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x36, 0xE2, 0xE8)),
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x11, 0x10, 0x2A)),
                FontWeight = FontWeights.Bold,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(10, 8, 10, 8)
            };
            ok.Click += (_, _) => TryPassword(m);
            ContentHost.Children.Add(ok);
            Dispatcher.BeginInvoke(new Action(() => _pwdBox?.Focus()));
        }
        else if (m.Kind == AuthMethodKind.System)
        {
            ContentHost.Children.Add(MakeNote("使用 Windows 系统凭据（PIN / Windows Hello / 指纹 / 密码）完成验证。"));
            var btn = MakeActionButton("使用系统解锁", "#7C3AED");
            btn.Click += (_, _) => TrySystem(m);
            ContentHost.Children.Add(btn);
        }
        else if (m.Kind == AuthMethodKind.Usb)
        {
            ContentHost.Children.Add(MakeNote("请插入已登记的 U 盘，然后点击扫描。"));
            var btn = MakeActionButton("扫描 U 盘", "#0EA5E9");
            btn.Click += (_, _) => TryUsb(m);
            ContentHost.Children.Add(btn);
        }
    }

    private static TextBlock MakeNote(string text) => new()
    {
        Text = text,
        Foreground = System.Windows.Media.Brushes.White,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 10)
    };

    private static Button MakeActionButton(string text, string color)
    {
        var brush = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
        return new Button
        {
            Content = text,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = brush,
            Foreground = System.Windows.Media.Brushes.White,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(10, 8, 10, 8)
        };
    }

    private bool IsLockHint() => HintText.Text != null && HintText.Text.Contains("锁定");

    // 密码验证
    private void TryPassword(AuthMethodEntry m)
    {
        if (_busy) return;
        var input = _pwdBox?.Password ?? "";
        var stored = m.GetPasswordOptions().PasswordHash;
        if (AuthService.TryUnlock(stored, input))
        {
            OnFactorSatisfied(AuthMethodKind.Password);
            return;
        }
        if (AuthService.IsLocked(out var rem))
        {
            ShowCountdown(TimeSpan.FromMinutes(rem));
            _lockTimer.Start();
        }
        else
        {
            AuthService.RecordFailure(out var left, out var minutes);
            if (left > 0)
            {
                HintText.Text = $"密码错误，你还有 {left} 次机会";
                HintText.Visibility = Visibility.Visible;
                _lockTimer.Stop();
            }
            else
            {
                ShowCountdown(TimeSpan.FromMinutes(minutes));
                _lockTimer.Start();
            }
        }
        if (_pwdBox != null) { _pwdBox.Password = ""; _pwdBox.Focus(); }
    }

    // 系统解锁（Windows Hello）
    private async void TrySystem(AuthMethodEntry m)
    {
        if (_busy) return;
        _busy = true;
        HintText.Text = "正在调用系统验证…";
        HintText.Visibility = Visibility.Visible;
        var (ok, msg) = await SystemUnlockService.VerifyAsync("易通遥需要验证以继续");
        _busy = false;
        if (ok)
        {
            AuthService.SetSystemUnlocked();
            OnFactorSatisfied(AuthMethodKind.System);
            return;
        }
        HintText.Text = msg;
        HintText.Visibility = Visibility.Visible;
    }

    // U 盘解锁
    private async void TryUsb(AuthMethodEntry m)
    {
        if (_busy) return;
        _busy = true;
        HintText.Text = "正在检测 U 盘…";
        HintText.Visibility = Visibility.Visible;
        var opts = m.GetUsbOptions();
        bool ok = await System.Threading.Tasks.Task.Run(
            () => UsbUnlockService.TryUnlock(opts.UseFileMode ? opts.TokenHash : opts.SerialHash,
                                             opts.UseFileMode));
        _busy = false;
        if (ok)
        {
            AuthService.SetUsbUnlocked();
            OnFactorSatisfied(AuthMethodKind.Usb);
            return;
        }
        HintText.Text = "未检测到已登记的 U 盘，请插入后重试。";
        HintText.Visibility = Visibility.Visible;
    }

    // 双因子与收尾
    private void OnFactorSatisfied(AuthMethodKind kind)
    {
        _passedKinds.Add(kind);
        _passedCategories.Add(AuthMethodKinds.Category(kind));
        if (!_requireTwo || _passedCategories.Count >= 2)
        {
            _lockTimer.Stop();
            DialogResult = true;
            Close();
            return;
        }
        HintText.Text = $"已通过{AuthMethodKinds.Label(kind)}，还需另一种不同类别的方式才能完成解锁。";
        HintText.Visibility = Visibility.Visible;
        var next = _methods.FirstOrDefault(m => !_passedKinds.Contains(m.Kind));
        if (next != null) MethodList.SelectedItem = next;
    }

    // 保密问题找回
    private void RenderRecovery()
    {
        var m = _methods.FirstOrDefault(x => x.Kind == AuthMethodKind.Password
                                             && !string.IsNullOrEmpty(x.GetPasswordOptions().SecurityQuestion));
        if (m == null) return;
        var opts = m.GetPasswordOptions();
        ContentHost.Children.Clear();
        _pwdBox = null;
        ContentHost.Children.Add(MakeNote("保密问题：" + opts.SecurityQuestion));
        _answerBox = new PasswordBox
        {
            Background = System.Windows.Media.Brushes.Transparent,
            Foreground = System.Windows.Media.Brushes.White,
            BorderBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x3a, 0x2a, 0x6e)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 8)
        };
        _answerBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) TryRecover(opts); };
        ContentHost.Children.Add(_answerBox);
        var btn = MakeActionButton("提交", "#3a2a6e");
        btn.Click += (_, _) => TryRecover(opts);
        ContentHost.Children.Add(btn);
        Dispatcher.BeginInvoke(new Action(() => _answerBox?.Focus()));
    }

    private void TryRecover(AuthPasswordOptions opts)
    {
        var a = _answerBox?.Password ?? "";
        if (AuthService.TryRecover(opts.SecurityAnswerHash, a))
        {
            _lockTimer.Stop();
            WantsPasswordReset = true;
            DialogResult = true;
            Close();
            return;
        }
        HintText.Text = "保密问题答案错误。";
        HintText.Visibility = Visibility.Visible;
        if (_answerBox != null) { _answerBox.Password = ""; _answerBox.Focus(); }
    }

    private void RefreshLockoutUi()
    {
        if (AuthService.IsLocked(out var remaining))
        {
            ShowCountdown(TimeSpan.FromMinutes(remaining));
            _lockTimer.Start();
        }
    }

    private void UpdateLockCountdown()
    {
        var rem = AuthService.GetLockRemaining();
        if (rem > TimeSpan.Zero) { ShowCountdown(rem); return; }
        AuthService.IsLocked(out _);
        _lockTimer.Stop();
        if (IsLockHint()) HintText.Visibility = Visibility.Collapsed;
    }

    private void ShowCountdown(TimeSpan rem)
    {
        HintText.Text = "账号已锁定，剩余锁定时间 " + FormatSpan(rem);
        HintText.Visibility = Visibility.Visible;
    }

    private static string FormatSpan(TimeSpan t)
    {
        int total = (int)Math.Ceiling(t.TotalSeconds);
        int m = total / 60;
        int s = total % 60;
        if (m > 0) return s == 0 ? $"{m} 分钟" : $"{m} 分 {s} 秒";
        return $"{s} 秒";
    }
}
