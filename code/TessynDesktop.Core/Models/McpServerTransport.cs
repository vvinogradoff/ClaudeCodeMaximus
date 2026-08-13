namespace TessynDesktop.Models;

/// <summary>How a registered External MCP Server (FR.20.1) is reached.</summary>
/// <remarks>Created by Claude</remarks>
public enum McpServerTransport
{
	/// <summary>Local process, launched via <see cref="ExternalMcpServerModel.Command"/>/<see cref="ExternalMcpServerModel.Arguments"/>.</summary>
	Stdio,

	/// <summary>Remote HTTP(S) MCP server, reached via <see cref="ExternalMcpServerModel.Url"/>.</summary>
	Http,
}
