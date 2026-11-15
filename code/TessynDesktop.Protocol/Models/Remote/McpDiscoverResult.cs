namespace TessynDesktop.Models.Remote;

/// <summary>Result of <c>mcp.discover</c>: the endpoints found (the ones already known are kept) and whether the daemon's time limit stopped the probe.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record McpDiscoverResult(McpDiscoveredConfigDto Config, bool TimedOut);

/// <summary>The OAuth endpoints a discovery found; an empty endpoint was not found.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record McpDiscoveredConfigDto(string AuthorizationEndpoint, string TokenEndpoint, string? RegistrationEndpoint, bool DiscoveryCompleted);

/// <summary>The endpoints a client already knows and wants kept, the <c>oauth</c> param of <c>mcp.discover</c>.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record McpDiscoverKnownEndpoints(string? AuthorizationEndpoint = null, string? TokenEndpoint = null, string? RegistrationEndpoint = null);

/// <summary>Result of <c>mcp.authorize</c>.</summary>
/// <param name="Authorized">The token was obtained and stored.</param>
/// <param name="Error">Why it was not, in a short sentence; never a token.</param>
/// <remarks>Created by Claude</remarks>
public sealed record McpAuthorizeResultDto(bool Authorized, string? Error = null);
