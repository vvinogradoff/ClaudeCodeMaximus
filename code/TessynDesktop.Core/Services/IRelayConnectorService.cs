using System;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// The desktop's outbound link to the web server (contract section 6): receives JSON-RPC requests over SSE, answers them
/// through the <see cref="TdcpDispatcher"/> and forwards remote events. A no-op while unconfigured or disabled.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IRelayConnectorService
{
	/// <summary>Current state.</summary>
	RelayStatusModel Status { get; }

	/// <summary>Raised (on a background thread) whenever <see cref="Status"/> changes.</summary>
	event Action<RelayStatusModel>? StatusChanged;

	/// <summary>Starts the connect loop if the stored config is enabled and complete; never throws.</summary>
	void Start();

	/// <summary>Stops the loop and waits (bounded) for it to finish; never throws.</summary>
	void Stop();

	/// <summary>Stop, then Start with the freshly stored config.</summary>
	void Restart();
}
