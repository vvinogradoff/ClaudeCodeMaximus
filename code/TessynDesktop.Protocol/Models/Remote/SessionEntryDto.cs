using System;

namespace TessynDesktop.Models.Remote;

/// <summary>One transcript entry (contract section 3.10).</summary>
/// <remarks>Created by Claude</remarks>
public sealed record SessionEntryDto
{
	/// <summary>Position in the parsed entry list.</summary>
	public int Index { get; init; }

	public DateTimeOffset Timestamp { get; init; }

	/// <summary>"user", "assistant", "system" or "compaction".</summary>
	public string Role { get; init; } = string.Empty;

	public string Content { get; init; } = string.Empty;

	/// <summary>True when <see cref="Content"/> was cut; omitted otherwise.</summary>
	public bool? Truncated { get; init; }

	/// <summary>Length of the uncut content; set only when <see cref="Truncated"/>.</summary>
	public int? FullLength { get; init; }

	public string? ProfileName { get; init; }
	public string? ModelId { get; init; }
	public string? Effort { get; init; }

	/// <summary>
	/// What produced a user entry: "scheduled", "orchestrated", "telegram" or "web". Omitted for the desktop user and for every
	/// other role. Additive: a client that does not know the field treats the entry as the user's own.
	/// </summary>
	public string? Source { get; init; }

	// P1.F (additive): stable entry identity and ranged reads.

	/// <summary>
	/// Stable id <c>{fileId}:{ordinal}</c> (see <see cref="EntryIds"/>); survives daemon restarts and is never reused. Omitted when
	/// the daemon has no content store.
	/// </summary>
	public string? EntryId { get; init; }

	/// <summary>Set only for a <c>session.entry</c> call with a <c>range</c>: where in the full content <see cref="Content"/> starts (characters).</summary>
	public int? ContentOffset { get; init; }
}
