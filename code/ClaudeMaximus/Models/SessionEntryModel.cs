using System;

namespace ClaudeMaximus.Models;

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

	public bool IsCompaction => Role == Constants.SessionFile.RoleCompaction;
}
