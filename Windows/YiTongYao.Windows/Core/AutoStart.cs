using System;
using System.IO;
using Microsoft.Win32;

namespace YiTongYao.Core;

/// <summary>开机自动启动管理。</summary>
internal static class AutoStart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "YiTongYao";
    public const string StartupArg = "--startup";

    /// <summary>当前可执行文件完整路径。</summary>
    public static string ExePath
    {
        get
        {
            var p = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(p) && File.Exists(p)) return p;
            return Path.Combine(AppContext.BaseDirectory, "YiTongYao.exe");
        }
    }

    /// <summary>写入注册表的完整命令行（路径加引号以兼容含空格的目录）。</summary>
    private static string CommandLine => "\"" + ExePath + "\" " + StartupArg;

    /// <summary>开机自启是否已启用。</summary>
    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            var value = key?.GetValue(ValueName) as string;
            return !string.IsNullOrWhiteSpace(value);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>启用 / 禁用开机自启。失败时抛出带说明的异常，由界面提示用户。</summary>
    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, true);
            if (key == null)
                throw new InvalidOperationException("无法打开或创建启动项注册表键");

            if (enabled)
            {
                key.SetValue(ValueName, CommandLine, RegistryValueKind.String);
            }
            else
            {
                // 不存在时 DeleteValue 会抛异常，统一吞掉
                try { key.DeleteValue(ValueName, false); }
                catch { }
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                enabled ? "设置开机自动启动失败：" + ex.Message : "取消开机自动启动失败：" + ex.Message, ex);
        }
    }

    /// <summary>按设置同步启动项。</summary>
    public static bool Sync(bool desired)
    {
        if (!desired) return false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            var current = key?.GetValue(ValueName) as string ?? "";
            if (string.Equals(current, CommandLine, StringComparison.OrdinalIgnoreCase))
                return false; // 已一致，无需写入

            SetEnabled(true);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
