namespace TessynDesktop.Models;

/// <summary>Running object count and byte total for one type (or one segment kind) while walking the heap.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class HeapTypeTotalsModel
{
	public long Count { get; set; }
	public ulong Bytes { get; set; }
}
