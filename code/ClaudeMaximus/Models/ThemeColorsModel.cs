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
    };
}
