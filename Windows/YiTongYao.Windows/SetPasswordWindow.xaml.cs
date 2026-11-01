using System.Collections.Generic;
using System.Linq;
using System.Windows;
using YiTongYao.Core;

namespace YiTongYao.Windows;

// 设置 / 修改密码
public partial class SetPasswordWindow : Window
{
    private readonly SettingsStore _s;
    private readonly AuthMethodEntry? _entry;

    internal SetPasswordWindow(SettingsStore s, AuthMethodEntry? entry)
    {
        InitializeComponent();
        _s = s;
        _entry = entry;
        TitleText.Text = entry == null ? "设置新密码（至少 4 位）" : "修改密码（至少 4 位）";
        Title = entry == null ? "设置密码" : "修改密码";

        var opts = entry?.GetPasswordOptions();
        if (!string.IsNullOrEmpty(opts?.SecurityQuestion))
        {
            QuestionCheck.IsChecked = true;
            QuestionBox.Text = opts!.SecurityQuestion;
        }
    }

    private void QuestionCheck_Changed(object sender, RoutedEventArgs e)
    {
        QuestionPanel.Visibility = QuestionCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowHint(string text)
    {
        HintText.Text = text;
        HintText.Visibility = Visibility.Visible;
        PwdBox.Password = "";
        ConfirmBox.Password = "";
    }

    private void OkBtn_Click(object sender, RoutedEventArgs e)
    {
        var pwd = PwdBox.Password;
        var confirm = ConfirmBox.Password;
        if (pwd.Length < 4) { ShowHint("密码至少 4 位。"); return; }
        if (pwd != confirm) { ShowHint("两次输入的密码不一致。"); return; }

        string q = "", ah = "";
        if (QuestionCheck.IsChecked == true)
        {
            q = (QuestionBox.Text ?? "").Trim();
            var a = AnswerBox.Password;
            if (q.Length == 0 || a.Length == 0) { ShowHint("请同时填写保密问题与答案。"); return; }
            ah = AuthService.HashSecret(a);
        }

        var newOpts = new AuthPasswordOptions
        {
            PasswordHash = AuthService.HashSecret(pwd),
            SecurityQuestion = q,
            SecurityAnswerHash = ah
        };

        if (_entry == null)
        {
            int count = _s.AuthMethods.Count(m => m.Kind == AuthMethodKind.Password);
            var entry = new AuthMethodEntry
            {
                Kind = AuthMethodKind.Password,
                Name = count == 0 ? "主密码" : "密码 " + (count + 1),
                Operations = AuthOperation.All.ToList()
            };
            entry.SetPasswordOptions(newOpts);
            _s.AuthMethods.Add(entry);
        }
        else
        {
            _entry.SetPasswordOptions(newOpts);
        }

        // 重设密码时一并清空失败计数与锁定
        _s.PasswordFailedAttempts = 0;
        _s.PasswordLockoutUntil = System.DateTime.MinValue;
        _s.PasswordLockoutLevel = 0;
        _s.Save();
        AuthService.ResetSession();

        DialogResult = true;
        Close();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
