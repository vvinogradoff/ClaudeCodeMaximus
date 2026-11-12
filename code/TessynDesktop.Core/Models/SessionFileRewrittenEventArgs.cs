using System;

namespace TessynDesktop.Models;

/// <summary>
/// Raised by <see cref="Services.ISessionFileService.SessionFileRewritten"/> after a session file was replaced as a whole
/// (rewrite, write from entries, compaction repair) — entries a client already holds may no longer exist.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionFileRewrittenEventArgs : EventArgs
{
	public required string FileName { get; init; }
}
