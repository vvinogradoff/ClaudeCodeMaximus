namespace TessynDesktop.Models.Remote;

/// <summary>Result of <c>recent.hide</c> (contract 3.7).</summary>
/// <param name="Hidden">False when the session is pinned (or unknown) and was left alone.</param>
/// <remarks>Created by Claude</remarks>
public sealed record RecentHideResult(bool Hidden);
