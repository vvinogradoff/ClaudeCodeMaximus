using System;
using System.Collections.Generic;
using System.Linq;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <inheritdoc cref="ISessionEntryReader" />
/// <remarks>Created by Claude</remarks>
public sealed class SessionEntryReader : ISessionEntryReader
{
	private readonly ISessionFileService _files;
	private readonly ISessionEpochTracker _epochs;
	private readonly ISessionContentStore? _store;
	private const string HistoryChangedMessage = "Session history changed";

	private readonly object _gate = new();
	private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
	private long _tick;

	/// <param name="store">
	/// Serves pages and single entries by byte range from the sidecar index (P1.E), so neither parses the whole file. Without
	/// one (stub-based tests) they fall back to the parsed-entry cache; <see cref="GetAll"/> always uses the cache.
	/// </param>
	public SessionEntryReader(ISessionFileService files, ISessionEpochTracker epochs, ISessionContentStore? store = null)
	{
		_files  = files;
		_epochs = epochs;
		_store  = store;
	}

	/// <inheritdoc />
	public IReadOnlyList<SessionEntryModel> GetAll(string fileName)
	{
		var stamp = _files.GetFileStamp(fileName);
		if (stamp == null)
		{
			lock (_gate)
				_cache.Remove(fileName);

			return [];
		}

		lock (_gate)
		{
			if (_cache.TryGetValue(fileName, out var hit) && hit.Stamp == stamp)
			{
				hit.LastUsed = ++_tick;
				return hit.Entries;
			}
		}

		// Parsed outside the lock: two parallel misses parse twice, the last result wins.
		var entries = _files.ReadEntries(fileName);

		lock (_gate)
		{
			_cache[fileName] = new CacheEntry(stamp, entries) { LastUsed = ++_tick };
			EvictOldest();
		}

		return entries;
	}

	/// <inheritdoc />
	public SessionEntriesResult GetPage(string nodeId, string fileName, int? beforeIndex, int? afterIndex, int limit)
	{
		// The epoch is read before the file: a rewrite in between makes the page older than its epoch says, never newer.
		var epoch = _epochs.GetEpoch(fileName);
		if (_store != null)
			return GetStorePage(nodeId, fileName, beforeIndex, afterIndex, limit, epoch, expectedFileId: null);

		var entries = GetAll(fileName);
		var total   = entries.Count;

		int start;
		int end;
		if (afterIndex.HasValue)
		{
			start = (int)Math.Clamp((long)afterIndex.Value + 1, 0, total);
			end   = Math.Min(total, start + limit);
		}
		else if (beforeIndex.HasValue)
		{
			end   = Math.Clamp(beforeIndex.Value, 0, total);
			start = Math.Max(0, end - limit);
		}
		else
		{
			end   = total;
			start = Math.Max(0, total - limit);
		}

		var page = Enumerable.Range(start, end - start)
			.Select(i => entries[i].ToDto(i, Constants.Remote.Limits.EntryContentMaxChars))
			.ToList();

		return new SessionEntriesResult(nodeId, epoch, total, page);
	}

	/// <inheritdoc />
	public SessionEntriesResult GetPage(string nodeId, string fileName, int? beforeIndex, int? afterIndex, int limit, Guid expectedFileId)
	{
		if (_store == null)
			throw new TdcpInvalidParamsException("Entry ids are not available");

		return GetStorePage(nodeId, fileName, beforeIndex, afterIndex, limit, _epochs.GetEpoch(fileName), expectedFileId);
	}

	/// <inheritdoc />
	public SessionEntryDto GetEntryById(string fileName, Guid fileId, int ordinal)
	{
		if (_store == null)
			throw new TdcpInvalidParamsException("Entry ids are not available");

		return ReadStoreEntry(fileName, ordinal, fileId);
	}

	/// <inheritdoc />
	public SessionContentHeaderModel? GetHeader(string fileName) => _store?.GetHeader(fileName);

	/// <inheritdoc />
	public SessionContentLastUsedModel? GetLastUsed(string fileName) => _store?.GetLastUsed(fileName);

	/// <inheritdoc />
	public SessionEntryDto GetEntry(string fileName, int index, int epoch)
	{
		if (epoch != _epochs.GetEpoch(fileName))
			throw new TdcpException("Session history changed");

		if (_store != null)
			return ReadStoreEntry(fileName, index, expectedFileId: null);

		var entries = GetAll(fileName);
		if (index < 0 || index >= entries.Count)
			throw new TdcpException("Entry not found");

		return entries[index].ToDto(index, int.MaxValue);
	}

	/// <summary>A page read from the store; the same window and content as the parsed-list path, without parsing the file.</summary>
	private SessionEntriesResult GetStorePage(string nodeId, string fileName, int? beforeIndex, int? afterIndex, int limit, int epoch, Guid? expectedFileId)
	{
		var page = _store!.ReadPage(fileName, new SessionContentPageRequest(beforeIndex, afterIndex, limit, int.MaxValue, int.MaxValue));
		if (expectedFileId.HasValue && page.FileId != expectedFileId.Value)
			throw new TdcpException(HistoryChangedMessage);

		var dtos = page.Entries
			.Select(e => e.Entry.ToDto(e.Ordinal, Constants.Remote.Limits.EntryContentMaxChars) with { EntryId = EntryIds.Format(page.FileId, e.Ordinal) })
			.ToList();

		return new SessionEntriesResult(nodeId, epoch, page.Total, dtos, page.FileId == Guid.Empty ? null : EntryIds.FormatFileId(page.FileId), page.Total);
	}

	/// <summary>
	/// One full entry through a one-entry page, so that its content and the file generation it belongs to come from the same
	/// snapshot of the index.
	/// </summary>
	private SessionEntryDto ReadStoreEntry(string fileName, int ordinal, Guid? expectedFileId)
	{
		if (ordinal < 0)
			throw new TdcpException("Entry not found");

		var page = _store!.ReadPage(fileName, new SessionContentPageRequest(null, ordinal - 1, 1, int.MaxValue, int.MaxValue));
		if (expectedFileId.HasValue && page.FileId != expectedFileId.Value)
			throw new TdcpException(HistoryChangedMessage);

		if (page.Entries.Count == 0 || page.Entries[0].Ordinal != ordinal)
			throw new TdcpException("Entry not found");

		return page.Entries[0].Entry.ToDto(ordinal, int.MaxValue) with { EntryId = EntryIds.Format(page.FileId, ordinal) };
	}

	private void EvictOldest()
	{
		while (_cache.Count > Constants.Remote.EntryCache.MaxFiles)
		{
			var oldest = _cache.MinBy(pair => pair.Value.LastUsed).Key;
			_cache.Remove(oldest);
		}
	}

	private sealed class CacheEntry(SessionFileStampModel stamp, IReadOnlyList<SessionEntryModel> entries)
	{
		public SessionFileStampModel Stamp { get; } = stamp;
		public IReadOnlyList<SessionEntryModel> Entries { get; } = entries;
		public long LastUsed { get; set; }
	}
}
