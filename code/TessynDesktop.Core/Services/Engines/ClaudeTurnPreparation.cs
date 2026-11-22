using TessynDesktop.Models;
using TessynDesktop.Models.Engines;

namespace TessynDesktop.Services;

/// <summary>The MCP config and Local Tool Profile a turn's claude process is launched with (formerly <c>TurnLaunch</c>).</summary>
/// <remarks>Created by Claude</remarks>
internal sealed record ClaudeTurnPreparation(string? McpConfigPath, LocalToolSpec? LocalToolSpec, bool StrictMcpConfig) : EngineTurnPreparation;
