using System;

namespace TessynDesktop.Models;

/// <summary>
/// Raised by <see cref="Services.IClaudeSessionIndexService.SessionIndexed"/> (and relayed by
/// <see cref="Services.IDaemonClient.SessionIndexed"/>) after the search index changed, so the UI can refresh search results.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionIndexedEventArgs : EventArgs
{
	/// <summary>The re-indexed session's external ID, or null when a bulk reindex changed one or more sessions.</summary>
	public string? ExternalId { get; init; }
}
