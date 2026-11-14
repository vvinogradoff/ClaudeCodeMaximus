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
	private readonly object _gate = new();
	private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
	private long _tick;

	public SessionEntryReader(ISessionFileService files, ISessionEpochTracker epochs)
	{
		_files  = files;
		_epochs = epochs;
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
		var epoch   = _epochs.GetEpoch(fileName);
		var entries = GetAll(fileName);
		var total   = entries.Count;

		int start;
		int end;
		if (afterIndex.HasValue)
		{
			start = Math.Clamp(afterIndex.Value + 1, 0, total);
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
	public SessionEntryDto GetEntry(string fileName, int index, int epoch)
	{
		if (epoch != _epochs.GetEpoch(fileName))
			throw new TdcpException("Session history changed");

		var entries = GetAll(fileName);
		if (index < 0 || index >= entries.Count)
			throw new TdcpException("Entry not found");

		return entries[index].ToDto(index, int.MaxValue);
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
