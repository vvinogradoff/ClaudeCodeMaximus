namespace TessynDesktop.Models.Remote;

/// <summary>
/// Params of <c>mcp.upsert</c>. Every member is optional on an update (a member left out keeps its value) and an empty string clears a text. A new
/// server needs <see cref="DisplayName"/> and, by its transport, a <see cref="Url"/> or a <see cref="Command"/>. The secret members
/// (<see cref="StaticHeaderValue"/>, <see cref="McpOAuthPatch.ClientSecret"/>) are write-only.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed record McpServerUpsertRequest(
	string? Id = null,
	string? DisplayName = null,
	bool? Enabled = null,
	McpTransportDto? Transport = null,
	string? Url = null,
	string? Command = null,
	string? Arguments = null,
	string? WorkingDirectory = null,
	McpAuthTypeDto? AuthType = null,
	string? StaticHeaderName = null,
	string? StaticHeaderValue = null,
	McpOAuthPatch? Oauth = null);

/// <summary>The OAuth members of <see cref="McpServerUpsertRequest"/>; the same rules (left out keeps, empty clears).</summary>
/// <remarks>Created by Claude</remarks>
public sealed record McpOAuthPatch(
	string? AuthorizationEndpoint = null,
	string? TokenEndpoint = null,
	string? RegistrationEndpoint = null,
	string? ClientId = null,
	string? ClientSecret = null,
	string? Scopes = null);
