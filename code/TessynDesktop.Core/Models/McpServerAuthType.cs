namespace TessynDesktop.Models;

/// <summary>How an <see cref="McpServerTransport.Http"/> External MCP Server entry authenticates (FR.20.1).</summary>
/// <remarks>Created by Claude</remarks>
public enum McpServerAuthType
{
	/// <summary>No authentication header is sent.</summary>
	None,

	/// <summary>A single fixed header (name/value) is sent on every request — e.g. a pre-shared bearer token.</summary>
	StaticHeader,

	/// <summary>OAuth 2.1 (PKCE); Tessyn is its own OAuth client (FR.20.3–FR.20.6).</summary>
	OAuth2,
}
