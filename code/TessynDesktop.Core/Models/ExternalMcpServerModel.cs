namespace TessynDesktop.Models;

/// <summary>
/// A single entry in the External MCP Server Registry (FR.20.1), persisted in
/// <c>AppSettingsModel.ExternalMcpServers</c>. Merged into every session's <c>--mcp-config</c>
/// alongside the in-process <c>cmx</c> server (FR.20.2) when <see cref="Enabled"/> is true.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ExternalMcpServerModel
{
	/// <summary>Stable identifier — used as the token-store filename and the <c>mcpServers</c> config key.</summary>
	public required string Id { get; init; }

	/// <summary>Human-readable name shown in Settings.</summary>
	public string DisplayName { get; set; } = string.Empty;

	/// <summary>Whether this entry is merged into <c>--mcp-config</c>. Disabled entries are omitted entirely.</summary>
	public bool Enabled { get; set; } = true;

	public McpServerTransport Transport { get; set; } = McpServerTransport.Http;

	// --- Stdio transport ---

	/// <summary>Executable/command to launch. <see cref="McpServerTransport.Stdio"/> only.</summary>
	public string Command { get; set; } = string.Empty;

	/// <summary>Command-line arguments. <see cref="McpServerTransport.Stdio"/> only.</summary>
	public string Arguments { get; set; } = string.Empty;

	/// <summary>Working directory for the launched process. <see cref="McpServerTransport.Stdio"/> only.</summary>
	public string WorkingDirectory { get; set; } = string.Empty;

	// --- Http transport ---

	/// <summary>Server base URL. <see cref="McpServerTransport.Http"/> only.</summary>
	public string Url { get; set; } = string.Empty;

	public McpServerAuthType AuthType { get; set; } = McpServerAuthType.None;

	/// <summary>Header name for <see cref="McpServerAuthType.StaticHeader"/> (e.g. "Authorization").</summary>
	public string StaticHeaderName { get; set; } = string.Empty;

	/// <summary>Header value for <see cref="McpServerAuthType.StaticHeader"/> (e.g. "Bearer &lt;token&gt;").</summary>
	public string StaticHeaderValue { get; set; } = string.Empty;

	/// <summary>OAuth client configuration. Only present/used when <see cref="AuthType"/> is <see cref="McpServerAuthType.OAuth2"/>.</summary>
	public McpOAuthConfig? OAuth { get; set; }
}
