using System;

namespace TessynDesktop.Models;

/// <summary>A link a session is "about": a Linear issue, a GitHub pull request or a GitHub repository (FR.24.1).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionTagModel
{
	public SessionTagKind Kind { get; set; }
	public string Key { get; set; } = string.Empty;          // canonical, e.g. "HSE-230", "tryhardstop/windows-app#215"
	public string Label { get; set; } = string.Empty;        // e.g. "HSE-230", "windows-app PR #215", "windows-app"
	public string Url { get; set; } = string.Empty;
	public SessionTagSource Source { get; set; }
	public DateTimeOffset? AddedUtc { get; set; }            // null for name-derived tags
}
