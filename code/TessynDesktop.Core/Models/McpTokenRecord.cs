using System;

namespace TessynDesktop.Models;

/// <summary>
/// The OAuth tokens for one External MCP Server Registry entry (FR.20.5). Serialized, DPAPI-encrypted,
/// and stored by <c>McpTokenStore</c> at <c>%AppData%\TessynDesktop\mcp-tokens\&lt;Id&gt;.token</c> —
/// never written to <c>appsettings.json</c>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class McpTokenRecord
{
	public required string AccessToken { get; set; }
	public string? RefreshToken { get; set; }
	public string TokenType { get; set; } = "Bearer";

	/// <summary>UTC expiry of <see cref="AccessToken"/>. Null if the server didn't report an expiry.</summary>
	public DateTimeOffset? ExpiresAtUtc { get; set; }
}
