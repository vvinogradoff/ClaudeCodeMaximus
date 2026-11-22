using System;
using Serilog;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// The entry id (FR.25, <c>&lt;fileId&gt;:&lt;ordinal&gt;</c>) of the USER entry a turn has just appended, so an engine can name it (Codex:
/// <c>clientUserMessageId</c>, FR.33.8). Another turn may append to the same file at the same moment, so the newest entries are checked for the
/// USER entry with the turn's text. Null when entry ids are not available or the entry is not found.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class UserEntryIdLocator
{
	private const int EntriesChecked = 8;

	private static readonly ILogger _log = Log.ForContext<UserEntryIdLocator>();

	private readonly ISessionEntryReader? _reader;

	public UserEntryIdLocator(ISessionEntryReader? reader) => _reader = reader;

	public string? Locate(string fileName, string content)
	{
		if (_reader is null)
			return null;

		try
		{
			var header = _reader.GetHeader(fileName);
			if (header is not { Exists: true })
				return null;

			for (var ordinal = header.EntryCount - 1; ordinal >= Math.Max(0, header.EntryCount - EntriesChecked); ordinal--)
			{
				var entry = _reader.GetEntryById(fileName, header.FileId, ordinal);
				if (entry.Role != Constants.SessionFile.RoleUser)
					continue;
				if (entry.Content == content || (entry.Truncated == true && content.StartsWith(entry.Content, StringComparison.Ordinal)))
					return EntryIds.Format(header.FileId, ordinal);
			}
		}
		catch (Exception ex) when (ex is not OutOfMemoryException)
		{
			_log.Debug(ex, "The USER entry id of {FileName} could not be read", fileName);
		}

		return null;
	}
}
