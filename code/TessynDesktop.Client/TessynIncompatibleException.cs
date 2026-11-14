using TessynDesktop.Models.Remote;

namespace TessynDesktop.Client;

/// <summary>The daemon no longer serves this client's protocol version.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TessynIncompatibleException : Exception
{
	public TessynIncompatibleException(HelloResult server)
		: base($"Daemon requires protocol {server.MinProtocolVersion} or newer; this client speaks {RemoteProtocol.ProtocolVersion}.")
	{
		Server = server;
	}

	/// <summary>What the daemon announced.</summary>
	public HelloResult Server { get; }
}
