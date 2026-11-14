using System.Collections.Generic;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// Daemon-owned access to a session transcript by entry, backed by a per-session sidecar index of byte ranges (P1.E).
/// Entries are parsed with the session-file parser and its FR.3.3 header rule, so a page equals the same slice of a full
/// parse. <c>(FileId, Ordinal)</c> is a stable entry id; ordinals are dense, so an ordinal is also the entry's index.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface ISessionContentStore
{
	/// <summary>O(1) summary of the file; <c>Exists = false</c> for a missing file.</summary>
	SessionContentHeaderModel GetHeader(string fileName);

	/// <summary>A page of entries by ordinal, ascending; empty for a missing file.</summary>
	SessionContentPageModel ReadPage(string fileName, SessionContentPageRequest request);

	/// <summary>One entry with its full content, or null when the ordinal does not exist.</summary>
	SessionContentEntryModel? ReadEntry(string fileName, int ordinal);

	/// <summary>
	/// A slice of the bytes of one entry, starting at <paramref name="startOffset"/> within it. Throws
	/// <see cref="TdcpException"/> when <paramref name="fileId"/> is not the file's current generation or the entry does not exist.
	/// </summary>
	SessionContentRangeModel ReadRange(string fileName, System.Guid fileId, int ordinal, long startOffset, int maxBytes);

	/// <summary>Drops the in-memory index of the file and rebuilds it (new <c>FileId</c>) on next use.</summary>
	void Invalidate(string fileName);

	/// <summary>Writes every sidecar that is behind its in-memory index.</summary>
	void Flush();
}
