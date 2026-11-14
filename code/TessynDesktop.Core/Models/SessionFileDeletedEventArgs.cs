using System;

namespace TessynDesktop.Models;

/// <summary>
/// Raised by <see cref="Services.ISessionFileService.SessionFileDeleted"/> after a session file was deleted — anything kept
/// beside it (such as the content store's entry index sidecar) is now an orphan.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionFileDeletedEventArgs : EventArgs
{
	public required string FileName { get; init; }
}
