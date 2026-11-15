using System.Threading;

namespace TessynDesktop.Services;

/// <summary>
/// The transport of the JSON-RPC call being handled, for the few methods whose policy differs between the loopback endpoint and
/// the relay (<c>file.read</c>, <c>file.preview</c>). Set by <see cref="TdcpDispatcher"/> around the handler call; flows with the
/// async call chain. Outside a dispatched call it reads as <see cref="TdcpTransport.Loopback"/>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class TdcpCallContext
{
	private static readonly AsyncLocal<TdcpTransport?> _current = new();

	/// <summary>The transport of the call in progress.</summary>
	public static TdcpTransport Transport => _current.Value ?? TdcpTransport.Loopback;

	internal static void Set(TdcpTransport transport) => _current.Value = transport;

	internal static void Clear() => _current.Value = null;
}
