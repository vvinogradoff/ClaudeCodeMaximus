namespace TessynDesktop.Services;

/// <summary>Compact role of an indexed entry; roles outside the known four are <see cref="Other"/> and read from the entry itself.</summary>
/// <remarks>Created by Claude</remarks>
internal enum EntryRoleCode : byte
{
	Other = 0,
	User = 1,
	Assistant = 2,
	System = 3,
	Compaction = 4,
}
