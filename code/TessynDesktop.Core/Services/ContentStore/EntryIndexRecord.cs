namespace TessynDesktop.Services;

/// <summary>One entry of the sidecar index: where its bytes are and what its header says.</summary>
/// <param name="Ordinal">Stable id within the file generation; equals the entry's position.</param>
/// <param name="Offset">Offset of the entry's header line.</param>
/// <param name="Length">Bytes from the header line to the next header line (or the end of the indexed part).</param>
/// <param name="TimestampTicks">UTC ticks of the header timestamp.</param>
/// <param name="Role">Role of the entry.</param>
/// <param name="Flags">Header metadata the entry carries.</param>
/// <remarks>Created by Claude</remarks>
internal readonly record struct EntryIndexRecord(
	int Ordinal,
	long Offset,
	int Length,
	long TimestampTicks,
	EntryRoleCode Role,
	EntryIndexFlags Flags);
