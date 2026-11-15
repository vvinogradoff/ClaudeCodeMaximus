using System;

namespace TessynDesktop.Models.Remote;

/// <summary>One chunk of a file (<c>file.read</c>).</summary>
/// <param name="Name">File name.</param>
/// <param name="Mime">MIME type from the extension.</param>
/// <param name="Size">File size in bytes.</param>
/// <param name="MtimeUtc">Last write time.</param>
/// <param name="Offset">Byte offset the chunk starts at; for text, moved forward off a partial UTF-8 character.</param>
/// <param name="Length">Bytes the chunk covers; the next chunk starts at <c>offset + length</c>.</param>
/// <param name="Encoding">"utf8" for text types, "base64" for binary.</param>
/// <param name="Data">The chunk, in <paramref name="Encoding"/>.</param>
/// <param name="Eof">True when the chunk reaches the end of the file.</param>
/// <remarks>Created by Claude</remarks>
public sealed record FileReadResult(
	string Name,
	string Mime,
	long Size,
	DateTimeOffset MtimeUtc,
	long Offset,
	int Length,
	string Encoding,
	string Data,
	bool Eof);
