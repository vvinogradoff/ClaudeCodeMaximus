namespace TessynDesktop.WPF.Services;

/// <summary>What the docking area starts from (FR.6.18): a saved layout, or the default one with the width and auto-hide taken over from the old tree settings.</summary>
/// <param name="Xml">The saved layout; null = the default layout.</param>
/// <param name="LeftWidth">Width of the left pane of the default layout.</param>
/// <param name="LeftAutoHidden">The left pane of the default layout starts auto-hidden (the old collapsed tree).</param>
/// <param name="Reason">Why the default layout is used (for the log); null when a saved layout is used.</param>
/// <remarks>Created by Claude</remarks>
public sealed record ShellLayoutStart(string? Xml, double LeftWidth, bool LeftAutoHidden, string? Reason);
