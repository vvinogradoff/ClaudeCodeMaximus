using System;

namespace TessynDesktop.Models;

/// <summary>
/// A single parsed entry from a session text file.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionEntryModel
{
	public required DateTimeOffset Timestamp { get; init; }
	public required string Role { get; init; }

	/// <summary>
	/// Message body. Empty string for COMPACTION separator entries.
	/// </summary>
	public required string Content { get; init; }

	/// <summary>Profile display name stored in the header for USER entries.</summary>
	public string? ProfileName { get; init; }

	/// <summary>Model ID stored in the header for ASSISTANT entries.</summary>
	public string? ModelId { get; init; }

	/// <summary>Effort level stored in the header for ASSISTANT entries.</summary>
	public string? Effort { get; init; }

	/// <summary>
	/// What produced a USER entry (<see cref="EntrySources"/>): "scheduled", "orchestrated", "telegram" or "web"; null for the
	/// desktop user and for every other role. A legacy "[Scheduled]" entry is reported as "scheduled".
	/// </summary>
	public string? Source { get; init; }

	public bool IsCompaction => Role == Constants.SessionFile.RoleCompaction;
}
