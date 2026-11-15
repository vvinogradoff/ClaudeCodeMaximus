using System;
using System.Collections.Generic;

namespace TessynDesktop.Services;

/// <summary>
/// Which connected clients present notifications themselves (package J2). A client registers channels (<c>toast</c>, <c>sound</c>) under its
/// <c>clientId</c>; a registration counts only while at least one local SSE stream opened with the same <c>clientId</c> is alive, so a client that
/// disconnects (or whose connection dies: the stream writes keep-alives) stops silencing the host without having unregistered.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IClientPresenceRegistry
{
	/// <summary>Raised when the answer of <see cref="IsPresented"/> may have changed for some channel. May be raised on any thread.</summary>
	event Action? Changed;

	/// <summary>
	/// Sets the channels of <paramref name="clientId"/>, replacing its earlier registration; an empty list unregisters. The channels must be
	/// among <c>RemoteProtocol.Notifications.Channels</c>.
	/// </summary>
	void Register(string clientId, IReadOnlyCollection<string> presents);

	/// <summary>A local stream of <paramref name="clientId"/> (null or blank: no client) is open until the returned handle is disposed.</summary>
	IDisposable OpenStream(string? clientId);

	/// <summary>True while a client that registered <paramref name="channel"/> has an open stream.</summary>
	bool IsPresented(string channel);
}
