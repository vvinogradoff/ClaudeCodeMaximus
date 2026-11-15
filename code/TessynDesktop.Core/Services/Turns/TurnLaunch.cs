using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>The MCP config and Local Tool Profile a turn's claude process is launched with.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed record TurnLaunch(string? McpConfigPath, LocalToolSpec? LocalToolSpec, bool StrictMcpConfig);
