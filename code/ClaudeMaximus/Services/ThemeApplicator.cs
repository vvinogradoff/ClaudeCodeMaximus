using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using ClaudeMaximus.Models;

namespace ClaudeMaximus.Services;

/// <remarks>Created by Claude</remarks>
public static class ThemeApplicator
{
    public const string KeyInputBg        = "CmxInputBg";
    public const string KeyInputFg        = "CmxInputFg";
    public const string KeyUserBubbleBg   = "CmxUserBubbleBg";
    public const string KeyUserBubbleFg   = "CmxUserBubbleFg";
    public const string KeyCodeBg         = "CmxCodeBg";
    public const string KeyCodeFg         = "CmxCodeFg";
    public const string KeyInlineCodeBg   = "CmxInlineCodeBg";
    public const string KeyInlineCodeFg   = "CmxInlineCodeFg";
    public const string KeySystemBubbleBg = "CmxSystemBubbleBg";

    public static void Apply(AppSettingsModel settings)
    {
        var app = Application.Current!;
        app.RequestedThemeVariant = settings.Theme switch
        {
            "Light" => ThemeVariant.Light,
            "Dark"  => ThemeVariant.Dark,
            _       => ThemeVariant.Default,
        };

        // Use dark colors when dark theme is active (either explicit or system dark)
        var isDark = settings.Theme == "Dark";
        ApplyColors(isDark ? settings.DarkColors : settings.LightColors);
    }

    public static void ApplyColors(ThemeColorsModel colors)
    {
        SetBrush(KeyInputBg,      colors.InputBoxBackground);
        SetBrush(KeyInputFg,      colors.InputBoxText);
        SetBrush(KeyUserBubbleBg, colors.UserBubbleBackground);
        SetBrush(KeyUserBubbleFg, colors.UserBubbleText);
        SetBrush(KeyCodeBg,       colors.CodeBlockBackground);
        SetBrush(KeyCodeFg,       colors.CodeBlockText);
        SetBrush(KeyInlineCodeBg,   colors.InlineCodeBackground);
        SetBrush(KeyInlineCodeFg,   colors.InlineCodeText);
        SetBrush(KeySystemBubbleBg, colors.SystemBubbleBackground);
    }

    private static void SetBrush(string key, string hex)
    {
        try { Application.Current!.Resources[key] = new SolidColorBrush(Color.Parse(hex)); }
        catch { /* invalid hex — skip */ }
    }
}
