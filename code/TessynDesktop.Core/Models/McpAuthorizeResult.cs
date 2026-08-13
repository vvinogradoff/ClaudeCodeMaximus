namespace TessynDesktop.Models;

/// <summary>Outcome of an interactive OAuth authorization attempt (FR.20.4).</summary>
/// <remarks>Created by Claude</remarks>
public sealed record McpAuthorizeResult(bool Success, string Message);
