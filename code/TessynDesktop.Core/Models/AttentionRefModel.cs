using System;

namespace TessynDesktop.Models;

/// <summary>
/// A reference stored with a Focus item (FR.23 amendment): an entry of a session transcript, a file or a URL. The daemon
/// resolves it when the item is posted. <see cref="AbsolutePath"/> stays in the ledger and is never sent to a client.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class AttentionRefModel
{
	/// <summary>"entry", "file" or "url" (see <see cref="Constants.Attention.RefKindEntry"/> and its siblings).</summary>
	public string Kind { get; set; } = string.Empty;

	public string? Label { get; set; }

	// ── entry ──
	public string? NodeId { get; set; }
	public string? FileId { get; set; }
	public string? EntryId { get; set; }
	public int? Offset { get; set; }
	public int? Length { get; set; }

	// ── file ──
	public string? RefId { get; set; }
	public string? Name { get; set; }

	/// <summary>Path relative to the session's working directory (forward slashes); null when the file lies outside it.</summary>
	public string? RelativePath { get; set; }

	public string? AbsolutePath { get; set; }
	public long? Size { get; set; }
	public string? Mime { get; set; }
	public DateTimeOffset? MtimeUtc { get; set; }

	// ── url ──
	public string? Url { get; set; }
}
