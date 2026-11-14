namespace TessynDesktop.Models;

/// <summary>An entry read from the store, with where its bytes live so a cut entry can be fetched on by range.</summary>
/// <param name="Ordinal">Stable id within the file's <c>FileId</c>; also the dense index of the entry.</param>
/// <param name="ByteOffset">Offset of the entry's header line in the session file.</param>
/// <param name="ByteLength">Length of the whole entry, header line included.</param>
/// <param name="Entry">The entry parsed by the session-file parser; its content is cut when <paramref name="Truncated"/>.</param>
/// <param name="Truncated">True when only the first <paramref name="ShownBytes"/> of the entry were read.</param>
/// <param name="ShownBytes">Bytes of the entry the content covers, counted from <paramref name="ByteOffset"/>; the next range read starts here.</param>
/// <remarks>Created by Claude</remarks>
public sealed record SessionContentEntryModel(
	int Ordinal,
	long ByteOffset,
	int ByteLength,
	SessionEntryModel Entry,
	bool Truncated,
	int ShownBytes);
