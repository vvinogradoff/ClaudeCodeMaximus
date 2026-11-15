namespace TessynDesktop.Models.Remote;

/// <summary>One registered external MCP server a local tool profile can include (no secrets).</summary>
/// <param name="Id">The registry id.</param>
/// <param name="DisplayName">The name shown in the checklist.</param>
/// <remarks>Created by Claude</remarks>
public sealed record LocalToolsMcpServerDto(string Id, string DisplayName);
