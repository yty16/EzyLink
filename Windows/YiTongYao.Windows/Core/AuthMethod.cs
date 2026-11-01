using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace YiTongYao.Core;

// 认证方式
public enum AuthMethodKind
{
    Password = 0,
    System = 1,
    Usb = 2
}

public sealed class AuthMethodEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public AuthMethodKind Kind { get; set; }
    public string Name { get; set; } = "";
    public string OptionsJson { get; set; } = "";
    public List<string> Operations { get; set; } = new();

    public AuthPasswordOptions GetPasswordOptions()
    {
        if (string.IsNullOrEmpty(OptionsJson)) return new AuthPasswordOptions();
        try { return JsonSerializer.Deserialize<AuthPasswordOptions>(OptionsJson) ?? new AuthPasswordOptions(); }
        catch { return new AuthPasswordOptions(); }
    }

    public void SetPasswordOptions(AuthPasswordOptions v) => OptionsJson = JsonSerializer.Serialize(v);

    public AuthUsbOptions GetUsbOptions()
    {
        if (string.IsNullOrEmpty(OptionsJson)) return new AuthUsbOptions();
        try { return JsonSerializer.Deserialize<AuthUsbOptions>(OptionsJson) ?? new AuthUsbOptions(); }
        catch { return new AuthUsbOptions(); }
    }

    public void SetUsbOptions(AuthUsbOptions v) => OptionsJson = JsonSerializer.Serialize(v);

    public override string ToString()
    {
        string n = string.IsNullOrEmpty(Name) ? AuthMethodKinds.Label(Kind) : Name + " · " + AuthMethodKinds.Label(Kind);
        return n + (Operations.Count > 0
            ? "  [" + string.Join("、", Operations.Select(AuthOperation.Label)) + "]"
            : "  [未保护任何操作]");
    }
}

public sealed class AuthPasswordOptions
{
    public string PasswordHash { get; set; } = "";
    public string SecurityQuestion { get; set; } = "";
    public string SecurityAnswerHash { get; set; } = "";
}

public sealed class AuthUsbOptions
{
    public bool UseFileMode { get; set; } = false;
    public string TokenHash { get; set; } = "";
    public string DriveLabel { get; set; } = "";
    public string SerialHash { get; set; } = "";
}

// 受保护的操作
public static class AuthOperation
{
    public const string OpenMain = "OpenMain";
    public const string Settings = "Settings";
    public const string StopService = "StopService";
    public const string Exit = "Exit";

    public static readonly string[] All = { OpenMain, Settings, StopService, Exit };

    public static string Label(string op) => op switch
    {
        OpenMain => "打开主窗口",
        Settings => "修改安全设置",
        StopService => "停止服务",
        Exit => "退出程序",
        _ => op
    };
}

public static class AuthMethodKinds
{
    public static string Label(AuthMethodKind k) => k switch
    {
        AuthMethodKind.Password => "密码",
        AuthMethodKind.System => "系统解锁（Windows Hello）",
        AuthMethodKind.Usb => "U 盘",
        _ => "未知"
    };

    // 双因子按「因子类别」区分：知识 / 平台 / 持有
    public static string Category(AuthMethodKind k) => k switch
    {
        AuthMethodKind.Password => "knowledge",
        AuthMethodKind.System => "platform",
        AuthMethodKind.Usb => "possession",
        _ => "other"
    };
}
