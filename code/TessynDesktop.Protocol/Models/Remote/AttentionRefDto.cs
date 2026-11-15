namespace TessynDesktop.Models.Remote;

/// <summary>
/// A reference on a Focus item (FR.23 amendment): an entry of a session transcript, a file or a URL. Never carries an absolute
/// path; a client reads a file through <see cref="RefId"/> and <c>file.read</c>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed record AttentionRefDto
{
	/// <summary>"entry", "file" or "url".</summary>
	public string Kind { get; init; } = string.Empty;

	/// <summary>Short text the posting session gave the reference, when it gave one.</summary>
	public string? Label { get; init; }

	// ── entry ──

	/// <summary>The session the entry belongs to.</summary>
	public string? NodeId { get; init; }

	/// <summary>Content generation of that session's file when the reference was resolved.</summary>
	public string? FileId { get; init; }

	/// <summary>Stable entry id <c>{fileId}:{ordinal}</c> (see <see cref="EntryIds"/>).</summary>
	public string? EntryId { get; init; }

	/// <summary>Where the quote starts in the entry's content (characters).</summary>
	public int? Offset { get; init; }

	/// <summary>Length of the quote in the entry's content (characters).</summary>
	public int? Length { get; init; }

	// ── file ──

	/// <summary>Opaque 128-bit handle (32 hex digits) for <c>file.read</c>.</summary>
	public string? RefId { get; init; }

	/// <summary>File name without a directory.</summary>
	public string? Name { get; init; }

	/// <summary>Path relative to the session's working directory, with forward slashes; omitted when the file lies outside it.</summary>
	public string? Path { get; init; }

	public long? Size { get; init; }
	public string? Mime { get; init; }
	public System.DateTimeOffset? MtimeUtc { get; init; }

	// ── url ──

	public string? Url { get; init; }
}
