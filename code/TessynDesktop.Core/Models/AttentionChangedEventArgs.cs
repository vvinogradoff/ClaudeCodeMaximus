using System;

namespace TessynDesktop.Models;

/// <summary>Raised when a session's Attention Ledger changed (FR.23.16).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class AttentionChangedEventArgs(string nodeId) : EventArgs
{
	public string NodeId { get; } = nodeId;
}
