using System.Collections.Generic;
using System.Threading.Tasks;

namespace TessynDesktop.Services;

/// <summary>
/// Loopback HTTP MCP server that exposes scheduling and orchestration tools to
/// claude processes launched by the app (FR.14.1). Each node is identified by
/// the X-CMX-Token header which is mapped to a NodeId + model context.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IAgentMcpServer
{
	/// <summary>Port the server is listening on. 0 until <see cref="Start"/> completes.</summary>
	int Port { get; }

	/// <summary>
	/// Starts the loopback HTTP listener. Does nothing if already running or if
	/// <c>AgentToolsEnabled</c> is false in settings.
	/// </summary>
	void Start();

	/// <summary>Stops the listener and releases resources.</summary>
	void Stop();

	/// <summary>
	/// Ensures a per-node MCP config JSON file exists at
	/// <c>%APPDATA%\TessynDesktop\mcp\&lt;nodeId&gt;.json</c>
	/// and returns its full path. Creates it if missing or stale (port changed).
	/// Returns null when <c>AgentToolsEnabled</c> is false.
	/// </summary>
	Task<string?> EnsureConfigFileAsync(string nodeId, string agentToken);

	/// <summary>
	/// Local Tool Profile variant (FR.12.21): writes a filtered <c>--mcp-config</c> file containing
	/// only the cmx server (when <paramref name="includeCmx"/>) and the External MCP Server Registry
	/// entries named in <paramref name="externalServerIds"/>, instead of every enabled entry. Returns
	/// null when nothing is selected, so the caller omits <c>--mcp-config</c> entirely.
	/// </summary>
	Task<string?> EnsureConfigFileAsync(string nodeId, string agentToken, bool includeCmx, IReadOnlyList<string> externalServerIds);
}
