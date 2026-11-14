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
	/// <see cref="GetPage(string, string, int?, int?, int)"/> that fails with "Session history changed" when the page was read
	/// from another content generation than <paramref name="expectedFileId"/> (P1.F: <c>afterSeq</c>, <c>beforeEntryId</c>); the
	/// check and the read use one snapshot. Throws <see cref="TdcpInvalidParamsException"/> without a content store.
	/// </summary>
	SessionEntriesResult GetPage(string nodeId, string fileName, int? beforeIndex, int? afterIndex, int limit, System.Guid expectedFileId);

	/// <summary>
	/// One entry with its full content, by its stable id (P1.F). Throws <see cref="TdcpException"/> "Session history changed" when
	/// <paramref name="fileId"/> is not the file's current generation, "Entry not found" when the ordinal does not exist, and
	/// <see cref="TdcpInvalidParamsException"/> without a content store.
	/// </summary>
	SessionEntryDto GetEntryById(string fileName, System.Guid fileId, int ordinal);

	/// <summary>O(1) content header (P1.G); null when the reader has no content store.</summary>
	SessionContentHeaderModel? GetHeader(string fileName);

	/// <summary>The metadata the last-used settings are resolved from (P1.G), without a full parse; null when the reader has no content store.</summary>
	SessionContentLastUsedModel? GetLastUsed(string fileName);

	/// <summary>
	/// One entry with its full content. Throws <see cref="TdcpException"/> when <paramref name="epoch"/> is not the
	/// file's current epoch ("Session history changed") or the index does not exist.
	/// </summary>
	SessionEntryDto GetEntry(string fileName, int index, int epoch);
}
