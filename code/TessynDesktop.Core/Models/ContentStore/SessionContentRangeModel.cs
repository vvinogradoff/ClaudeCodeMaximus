using System;

namespace TessynDesktop.Models;

/// <summary>A slice of one entry's bytes, cut on character boundaries.</summary>
/// <param name="FileId">Content generation the slice was read from.</param>
/// <param name="Ordinal">Entry the slice belongs to.</param>
/// <param name="StartOffset">Offset of the slice within the entry (0 is its header line).</param>
/// <param name="NextOffset">Where the next slice starts; equals <paramref name="TotalBytes"/> at the end.</param>
/// <param name="TotalBytes">Length of the whole entry.</param>
/// <param name="Text">The slice, decoded.</param>
/// <remarks>Created by Claude</remarks>
public sealed record SessionContentRangeModel(
	Guid FileId,
	int Ordinal,
	long StartOffset,
	long NextOffset,
	long TotalBytes,
	string Text)
{
	public bool IsEnd => NextOffset >= TotalBytes;
}
