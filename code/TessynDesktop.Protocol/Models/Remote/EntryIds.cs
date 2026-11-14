using System;
using System.Globalization;

namespace TessynDesktop.Models.Remote;

/// <summary>
/// The wire form of a stable entry id: <c>{fileId}:{ordinal}</c>, with <c>fileId</c> as 32 lowercase hex digits. The ordinal is the
/// entry's dense index within that content generation of the session file.
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class EntryIds
{
	private const int FileIdLength = 32;

	/// <summary>The wire form of a file id (32 lowercase hex digits, no dashes).</summary>
	public static string FormatFileId(Guid fileId) => fileId.ToString("N");

	/// <summary>The wire form of an entry id.</summary>
	public static string Format(Guid fileId, int ordinal) =>
		FormatFileId(fileId) + ":" + ordinal.ToString(CultureInfo.InvariantCulture);

	/// <summary>Parses a wire file id; false for anything that is not 32 hex digits.</summary>
	public static bool TryParseFileId(string? text, out Guid fileId)
	{
		fileId = Guid.Empty;
		return text is { Length: FileIdLength } && Guid.TryParseExact(text, "N", out fileId);
	}

	/// <summary>Parses a wire entry id; false for anything malformed (including a negative ordinal).</summary>
	public static bool TryParse(string? text, out Guid fileId, out int ordinal)
	{
		fileId  = Guid.Empty;
		ordinal = 0;
		if (text == null || text.Length <= FileIdLength + 1 || text[FileIdLength] != ':')
			return false;

		return TryParseFileId(text[..FileIdLength], out fileId)
			&& int.TryParse(text.AsSpan(FileIdLength + 1), NumberStyles.None, CultureInfo.InvariantCulture, out ordinal);
	}
}
