namespace ClaudeMaximus.Models;

/// <remarks>Created by Claude</remarks>
public sealed class ThemeColorsModel
{
    public string InputBoxBackground   { get; set; } = "#FFFFFF";
    public string InputBoxText         { get; set; } = "#000000";
    public string UserBubbleBackground { get; set; } = "#1E3A5F";
    public string UserBubbleText       { get; set; } = "#FFFFFF";
    public string CodeBlockBackground  { get; set; } = "#F5F5F5";
    public string CodeBlockText        { get; set; } = "#202020";
    public string InlineCodeBackground    { get; set; } = "#E8E8E8";
    public string InlineCodeText         { get; set; } = "#202020";
    public string SystemBubbleBackground { get; set; } = "#BFE0F7";

    /// <summary>Session tree recency bar: last prompt within 15 minutes.</summary>
    public string Recency15MinBackground { get; set; } = "#90EE90";

    /// <summary>Session tree recency bar: last prompt within 30 minutes.</summary>
    public string Recency30MinBackground { get; set; } = "#3CB371";

    /// <summary>Session tree recency bar: last prompt within 1 hour.</summary>
    public string Recency60MinBackground { get; set; } = "#2E8B57";

    public static ThemeColorsModel DefaultDark() => new()
    {
        InputBoxBackground     = "#1E1E1E",
        InputBoxText           = "#D4D4D4",
        UserBubbleBackground   = "#1E3A5F",
        UserBubbleText         = "#FFFFFF",
        CodeBlockBackground    = "#2D2D2D",
        CodeBlockText          = "#D4D4D4",
        InlineCodeBackground   = "#252525",
        InlineCodeText         = "#D4D4D4",
        SystemBubbleBackground = "#1A3A50",
        Recency15MinBackground = "#2D5A3D",
        Recency30MinBackground = "#1E4D2B",
        Recency60MinBackground = "#15381F",
    };
}
