using System;

namespace TessynDesktop.Services;

/// <summary>Header metadata an indexed entry carries.</summary>
/// <remarks>Created by Claude</remarks>
[Flags]
internal enum EntryIndexFlags : byte
{
	None = 0,
	HasProfile = 1,
	HasModel = 2,
	HasEffort = 4,
}
