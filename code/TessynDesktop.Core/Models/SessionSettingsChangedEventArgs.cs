using System;

namespace TessynDesktop.Models;

/// <summary>
/// Raised (via <see cref="Services.ISessionRegistry.SessionSettingsChanged"/>, relayed by
/// <see cref="Services.IDaemonClient.SessionSettingsChanged"/>) after a session's settings (favourite, streaming mode)
/// were changed by a daemon method rather than by the desktop UI, so open view-models can refresh.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionSettingsChangedEventArgs : EventArgs
{
	public required string NodeId { get; init; }
}
