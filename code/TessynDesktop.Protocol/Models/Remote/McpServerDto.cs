using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>How an MCP server is reached (wire form of the registry's transport).</summary>
/// <remarks>Created by Claude</remarks>
public enum McpTransportDto
{
	Stdio = 0,
	Http = 1,
}

/// <summary>How an HTTP MCP server authenticates (wire form of the registry's auth type).</summary>
/// <remarks>Created by Claude</remarks>
public enum McpAuthTypeDto
{
	None = 0,
	StaticHeader = 1,
	Oauth2 = 2,
}

/// <summary>The OAuth state of a registered server.</summary>
/// <remarks>Created by Claude</remarks>
public enum McpServerStatusDto
{
	/// <summary>Not an OAuth server: there is nothing to authorize.</summary>
	NotApplicable = 0,

	/// <summary>An OAuth server without a stored token: run <c>mcp.authorize</c>.</summary>
	NotConnected = 1,

	/// <summary>An OAuth server with a stored token.</summary>
	Connected = 2,

	/// <summary>The token could not be refreshed: run <c>mcp.authorize</c> again.</summary>
	NeedsReauthorization = 3,
}

/// <summary>
/// A registered MCP server as <c>mcp.list</c> reports it. Secrets are write-only: the static header value, the client secret and the tokens never
/// appear, only <c>hasStaticHeaderValue</c> and <c>hasClientSecret</c>. A URL is shown without user info, query and fragment (<see cref="UrlHasQuery"/> says
/// there was a query); the command line arguments are not shown either (<see cref="HasArguments"/>), as either can carry a key.
/// </summary>
/// <param name="Id">Stable id (also the token file name).</param>
/// <param name="DisplayName">Name shown in Settings.</param>
/// <param name="Enabled">Whether the server is merged into every session's MCP config.</param>
/// <param name="Transport">Stdio (a local command) or Http.</param>
/// <param name="Url">The server URL without user info, query and fragment. Http only.</param>
/// <param name="UrlHasQuery">The stored URL has a query string (not shown).</param>
/// <param name="Command">The command to launch. Stdio only.</param>
/// <param name="HasArguments">The command has arguments (not shown).</param>
/// <param name="WorkingDirectory">The working directory of the command. Stdio only.</param>
/// <param name="AuthType">None, a static header or OAuth 2.1.</param>
/// <param name="StaticHeaderName">The header name of a static-header server.</param>
/// <param name="HasStaticHeaderValue">A header value is stored.</param>
/// <param name="OAuth">The OAuth client configuration of an OAuth server.</param>
/// <param name="Authorized">An OAuth server whose token is stored and usable.</param>
/// <param name="Status">See <see cref="McpServerStatusDto"/>.</param>
/// <param name="ConnectedIdentity">The account name the server reported when it was authorized, when it did.</param>
/// <remarks>Created by Claude</remarks>
public sealed record McpServerDto(
	string Id,
	string DisplayName,
	bool Enabled,
	McpTransportDto Transport,
	string? Url,
	bool UrlHasQuery,
	string? Command,
	bool HasArguments,
	string? WorkingDirectory,
	McpAuthTypeDto AuthType,
	string? StaticHeaderName,
	bool HasStaticHeaderValue,
	McpOAuthDto? Oauth,
	bool Authorized,
	McpServerStatusDto Status,
	string? ConnectedIdentity);

/// <summary>The OAuth client configuration of a server; the client secret is not shown.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record McpOAuthDto(
	string AuthorizationEndpoint,
	string TokenEndpoint,
	string? RegistrationEndpoint,
	string ClientId,
	bool HasClientSecret,
	string Scopes,
	bool DiscoveryCompleted);

/// <summary>Result of <c>mcp.list</c>.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record McpListResult(IReadOnlyList<McpServerDto> Servers);

/// <summary>Result of <c>mcp.remove</c>.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record McpRemoveResult(bool Removed);

/// <summary>Payload of <c>mcp.changed</c>: the server that changed, or none when the whole registry did.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record McpChangedPayload(string? Id = null);
