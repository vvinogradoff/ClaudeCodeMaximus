using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>Result of <c>tree.snapshot</c> (contract 3.2).</summary>
/// <param name="Version">Structural tree version read BEFORE the snapshot was built.</param>
/// <param name="Unchanged">True (and <paramref name="Directories"/> omitted) when the caller's <c>sinceVersion</c> equals <paramref name="Version"/>.</param>
/// <param name="Directories">The tree; omitted when <paramref name="Unchanged"/>.</param>
/// <remarks>Created by Claude</remarks>
public sealed record TreeSnapshotResult(long Version, bool? Unchanged, IReadOnlyList<TreeDirectoryDto>? Directories);
