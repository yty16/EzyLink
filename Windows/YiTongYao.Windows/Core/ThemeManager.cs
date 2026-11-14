using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace YiTongYao.Core;

/// <summary>主题管理：切换明暗调色板与自定义强调色。</summary>
internal static class ThemeManager
{
    public enum ThemeMode { System, Light, Dark }

    // 基础调色板（深 / 浅）
    private static readonly Dictionary<string, (string Dark, string Light)> Palette = new()
    {
        ["AppBg"]         = ("#1b1136", "#f5f5fa"),
        ["AppSurface"]    = ("#251846", "#ffffff"),
        ["AppSurfaceAlt"] = ("#2a2350", "#eceaf6"),
        ["AppControlBg"]  = ("#3a2a6e", "#ffffff"),
        ["AppBorder"]     = ("#3a2a6e", "#d6d2ec"),
        ["AppFg"]         = ("#ECEAFF", "#1b1136"),
        ["AppFgMuted"]    = ("#9C93C9", "#5f6b7a"),
        ["AppFgDim"]      = ("#7d73a8", "#8a86a8"),
        ["AppDanger"]     = ("#FF6B6B", "#B00020"),
        ["AppDangerBg"]   = ("#6e2a3a", "#f3d6da"),
        ["AppLogBg"]      = ("#160d2e", "#f0eef8"),
        ["AppLogFg"]      = ("#bdb4e8", "#4a4570"),
    };

    private static ThemeMode _currentMode = ThemeMode.System;
    private static string? _customAccent;

    public static void ApplyTheme(ThemeMode mode, string? accentHex)
    {
        _currentMode = mode;
        _customAccent = string.IsNullOrWhiteSpace(accentHex) ? null : accentHex;
        bool light = ResolveIsLight(mode);
        var res = Application.Current.Resources;

        foreach (var kv in Palette)
        {
            string hex = light ? kv.Value.Light : kv.Value.Dark;
            res[kv.Key] = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)!);
        }

        // 强调色：未自定义时跟随明暗
        string accent = _customAccent ?? (light ? "#0a8a8f" : "#36E2E8");
        string accent2 = light ? "#6a3fb0" : "#965CE9";
        res["AppAccent"]   = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(accent)!);
        res["AppAccent2"]  = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(accent2)!);
        res["AppAccentFg"] = new SolidColorBrush(IsDark(accent) ? Colors.White
                                          : (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#11102A")!);
        res["BrandBrush"] = new LinearGradientBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(accent)!,
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(accent2)!,
            new System.Windows.Point(0, 0), new System.Windows.Point(1, 1));
    }

    /// <summary>仅切换强调色（用于设置页实时预览，不改变明暗模式）。</summary>
    public static void SetAccent(string? accentHex) => ApplyTheme(_currentMode, accentHex);

    /// <summary>订阅系统主题变更：当模式为「跟随系统」时，系统切深/浅色后自动重应用。</summary>
    public static void RegisterSystemThemeWatcher()
    {
        try
        {
            SystemEvents.UserPreferenceChanged += (_, e) =>
            {
                if (e.Category != UserPreferenceCategory.General) return;
                if (_currentMode != ThemeMode.System) return;
                Application.Current?.Dispatcher.Invoke(() => ApplyTheme(_currentMode, _customAccent));
            };
        }
        catch { }
    }

    public static ThemeMode ParseMode(string? value) => value switch
    {
        "light" => ThemeMode.Light,
        "dark"  => ThemeMode.Dark,
        _       => ThemeMode.System
    };

    private static bool ResolveIsLight(ThemeMode mode)
    {
        if (mode == ThemeMode.Light) return true;
        if (mode == ThemeMode.Dark) return false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int v) return v == 1;
        }
        catch { }
        return false; // 默认深色
    }

    private static bool IsDark(string hex)
    {
        var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)!;
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double lum = 0.2126 * r + 0.7152 * g + 0.0722 * b;
        return lum < 0.5;
    }
}
