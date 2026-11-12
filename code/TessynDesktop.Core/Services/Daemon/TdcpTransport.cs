namespace TessynDesktop.Services;

/// <summary>Where a JSON-RPC request came from. The relay may only call methods flagged remote-callable.</summary>
/// <remarks>Created by Claude</remarks>
public enum TdcpTransport
{
	/// <summary>The local, token-authenticated /tdcp endpoint: every registered method is available.</summary>
	Loopback,

	/// <summary>The outbound relay link to the web server: only remote-callable methods are available.</summary>
	Relay,
}
