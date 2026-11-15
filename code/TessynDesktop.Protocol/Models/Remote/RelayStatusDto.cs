using System;

namespace TessynDesktop.Models.Remote;

/// <summary>The state of the relay connector (wire form).</summary>
/// <remarks>Created by Claude</remarks>
public enum RelayStateDto
{
	Disabled = 0,
	Connecting = 1,
	Connected = 2,
	Error = 3,
}

/// <summary>Result of the relay methods. The link token is write-only.</summary>
/// <param name="Enabled">The link is switched on.</param>
/// <param name="State">The connector's state.</param>
/// <param name="Url">The web server's base URL (no user info, query or fragment).</param>
/// <param name="HasToken">A link token is stored.</param>
/// <param name="Since">When the connector entered <paramref name="State"/>.</param>
/// <param name="LastError">Why the connector is in the error state.</param>
/// <remarks>Created by Claude</remarks>
public sealed record RelayStatusDto(bool Enabled, RelayStateDto State, string? Url, bool HasToken, DateTimeOffset Since, string? LastError = null);

/// <summary>Params of <c>relay.configure</c>; a member left out keeps its value.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record RelayConfigureRequest(bool? Enabled = null, string? Url = null, string? LinkToken = null);
