using System.Collections.Generic;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// Paged access to a session's transcript for remote clients. The parsed entry list of a file is cached and
/// revalidated against the file's length and last write time on every call.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface ISessionEntryReader
{
	/// <summary>Every parsed entry of the file (empty ones included); an empty list for a missing file.</summary>
	IReadOnlyList<SessionEntryModel> GetAll(string fileName);

	/// <summary>
	/// A page of at most <paramref name="limit"/> entries, ascending by index: entries after <paramref name="afterIndex"/>,
	/// entries before <paramref name="beforeIndex"/>, or, with neither, the last ones. Content is capped at
	/// <c>Constants.Remote.Limits.EntryContentMaxChars</c>.
	/// </summary>
	SessionEntriesResult GetPage(string nodeId, string fileName, int? beforeIndex, int? afterIndex, int limit);

	/// <summary>
	/// One entry with its full content. Throws <see cref="TdcpException"/> when <paramref name="epoch"/> is not the
	/// file's current epoch ("Session history changed") or the index does not exist.
	/// </summary>
	SessionEntryDto GetEntry(string fileName, int index, int epoch);
}
