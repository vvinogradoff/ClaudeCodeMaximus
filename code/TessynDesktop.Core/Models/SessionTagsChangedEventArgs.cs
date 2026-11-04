using System;

namespace TessynDesktop.Models;

/// <summary>Raised when a session's tags changed (FR.24.7); a null node id means every session may be affected.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionTagsChangedEventArgs(string? nodeId) : EventArgs
{
	public string? NodeId { get; } = nodeId;                 // null = every session may be affected (a Linear team was learned)
}
