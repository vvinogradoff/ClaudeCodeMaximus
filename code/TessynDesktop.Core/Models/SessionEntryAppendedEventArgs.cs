using System;

namespace TessynDesktop.Models;

/// <summary>
/// Raised by <see cref="Services.ISessionFileService.EntryAppended"/> (and relayed by
/// <see cref="Services.IDaemonClient.EntryAppended"/>) whenever a single entry is appended to a
/// session file (FR.21.9) — the tap point external mirrors (the Telegram bridge) subscribe to
/// instead of integrating with each turn-execution path separately. Not raised by bulk rewrites
/// (<c>RewriteSessionFile</c>/<c>WriteSessionFile</c>, session import), since those replace history
/// rather than append to it.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionEntryAppendedEventArgs : EventArgs
{
	public required string FileName { get; init; }
	public required SessionEntryModel Entry { get; init; }
}
