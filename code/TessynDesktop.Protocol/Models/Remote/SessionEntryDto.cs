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
}
