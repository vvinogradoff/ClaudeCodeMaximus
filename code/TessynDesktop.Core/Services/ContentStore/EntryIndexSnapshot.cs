using System;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// Immutable view of a file's index. <see cref="Records"/> may be shared with newer snapshots, which only write past
/// <see cref="Count"/>; a change of an existing record always copies the array first.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class EntryIndexSnapshot
{
	public required Guid FileId { get; init; }
	public required EntryIndexRecord[] Records { get; init; }
	public required int Count { get; init; }

	/// <summary>Length of the file the records cover.</summary>
	public required long IndexedLength { get; init; }

	/// <summary>Last write time (UTC ticks) the file had when the index last agreed with it.</summary>
	public required long LastWriteTicks { get; init; }

	public required long ContentVersion { get; init; }
	public required ulong Fingerprint { get; init; }
	public string? LastProfileName { get; init; }
	public string? LastModelId { get; init; }
	public string? LastEffort { get; init; }

	public int NextOrdinal => Count == 0 ? 0 : Records[Count - 1].Ordinal + 1;

	public bool Matches(SessionFileStampModel stamp) =>
		stamp.Length == IndexedLength && stamp.LastWriteUtc.UtcTicks == LastWriteTicks;

	public SessionContentHeaderModel ToHeader() => new(
		true,
		FileId,
		Count,
		NextOrdinal,
		Count == 0 ? null : new DateTimeOffset(Records[0].TimestampTicks, TimeSpan.Zero),
		Count == 0 ? null : new DateTimeOffset(Records[Count - 1].TimestampTicks, TimeSpan.Zero),
		LastProfileName,
		LastModelId,
		LastEffort,
		ContentVersion,
		IndexedLength);
}
