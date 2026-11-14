using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Serilog;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// Owns the entry indexes of all session files: loads a sidecar or builds the index, keeps it current by indexing only an
/// appended tail, rebuilds it (with a new file id) when the file changed any other way, and persists it. Updates of one file
/// run under that file's lock; everything else works on the immutable snapshot an update publishes.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class EntryIndexCatalog : IDisposable
{
	private static readonly ILogger _log = Log.ForContext<EntryIndexCatalog>();

	/// <summary>How long <see cref="Dispose"/> waits for the background indexer to finish its current file.</summary>
	private static readonly TimeSpan DisposeWait = TimeSpan.FromSeconds(5);

	private readonly ISessionFileService _files;
	private readonly string _directory;
	private readonly SessionFileScanner _scanner = new();
	private readonly EntryIndexFingerprint _fingerprint = new();
	private readonly EntryIndexFileCodec _codec = new();
	private readonly ConcurrentDictionary<string, FileIndexState> _states = new(StringComparer.OrdinalIgnoreCase);
	private readonly object _evictGate = new();

	// Files whose tail still has to be indexed after an append; one worker drains it so an appender never waits for an index.
	private readonly Channel<string> _appended = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
	private readonly ConcurrentDictionary<string, byte> _queued = new(StringComparer.OrdinalIgnoreCase);
	private readonly Task _indexer;
	private long _tick;

	public EntryIndexCatalog(ISessionFileService files, string directory)
	{
		_files     = files;
		_directory = directory;

		_files.EntryAppended        += OnEntryAppended;
		_files.SessionFileRewritten += OnSessionFileRewritten;
		_files.SessionFileDeleted   += OnSessionFileDeleted;

		_indexer = Task.Run(IndexAppendedAsync);
	}

	public void Dispose()
	{
		_files.EntryAppended        -= OnEntryAppended;
		_files.SessionFileRewritten -= OnSessionFileRewritten;
		_files.SessionFileDeleted   -= OnSessionFileDeleted;

		_appended.Writer.TryComplete();
		try
		{
			_indexer.Wait(DisposeWait);
		}
		catch (AggregateException)
		{
			// The worker never throws; nothing to report on shutdown.
		}

		Flush();
	}

	/// <summary>The current index of the file, brought up to date first; null when the session file does not exist.</summary>
	public EntryIndexSnapshot? Acquire(string fileName, bool rebuild = false)
	{
		var stamp = _files.GetFileStamp(fileName);
		if (stamp == null)
		{
			Forget(fileName);
			return null;
		}

		var state = _states.GetOrAdd(fileName, _ => new FileIndexState());
		state.LastUsed = Interlocked.Increment(ref _tick);

		var snapshot = state.Snapshot;
		if (snapshot != null && !rebuild && !state.ForceRebuild && snapshot.Matches(stamp))
			return snapshot;

		EntryIndexSnapshot? result;
		lock (state.Gate)
			result = Refresh(fileName, state, rebuild);

		EvictSurplus();
		return result;
	}

	/// <summary>Forgets the in-memory index and its sidecar; the next read rebuilds with a new file id.</summary>
	public void Invalidate(string fileName)
	{
		if (_states.TryGetValue(fileName, out var state))
		{
			lock (state.Gate)
			{
				state.ForceRebuild = true;
				state.Dirty        = false;
			}
		}

		DeleteSidecar(fileName);
	}

	/// <summary>Writes every sidecar that is behind its in-memory index.</summary>
	public void Flush()
	{
		foreach (var (fileName, state) in _states)
		{
			lock (state.Gate)
				PersistIfDirty(fileName, state);
		}
	}

	private void OnEntryAppended(object? sender, SessionEntryAppendedEventArgs e)
	{
		// Only files whose index is already loaded are kept current eagerly; a never-read session is indexed on first read.
		// The work itself runs on the indexer thread: the appender must neither wait for a rebuild holding the file's gate nor
		// ever see an exception from here (review B1). A skipped update is harmless, every read validates the index.
		try
		{
			if (_states.TryGetValue(e.FileName, out var state) && state.Snapshot != null && _queued.TryAdd(e.FileName, 0))
				_appended.Writer.TryWrite(e.FileName);
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "EntryIndexCatalog: could not queue the index update of {File}", e.FileName);
		}
	}

	private async Task IndexAppendedAsync()
	{
		await foreach (var fileName in _appended.Reader.ReadAllAsync().ConfigureAwait(false))
		{
			_queued.TryRemove(fileName, out _);
			try
			{
				Acquire(fileName);
			}
			catch (Exception ex)
			{
				// The next read validates the index against the file again.
				_log.Warning(ex, "EntryIndexCatalog: eager index update of {File} failed", fileName);
			}
		}
	}

	private void OnSessionFileRewritten(object? sender, SessionFileRewrittenEventArgs e) => Invalidate(e.FileName);

	private void OnSessionFileDeleted(object? sender, SessionFileDeletedEventArgs e) => Forget(e.FileName);

	/// <summary>Drops the in-memory index of a file that no longer exists, together with its sidecar and any temp file (review B3).</summary>
	private void Forget(string fileName)
	{
		_states.TryRemove(fileName, out _);
		DeleteSidecar(fileName);
	}

	private void DeleteSidecar(string fileName)
	{
		var path = SidecarPath(fileName);
		TryDelete(path);
		TryDelete(path + Constants.ContentStore.TempFileExtension);
	}

	private EntryIndexSnapshot? Refresh(string fileName, FileIndexState state, bool rebuild)
	{
		var stamp = _files.GetFileStamp(fileName);
		if (stamp == null)
		{
			Forget(fileName);
			return null;
		}

		var snapshot = state.Snapshot;
		if (snapshot == null && !rebuild && !state.ForceRebuild)
			snapshot = LoadSidecar(fileName);

		if (snapshot != null && !rebuild && !state.ForceRebuild && snapshot.Matches(stamp))
		{
			state.Snapshot = snapshot;
			return snapshot;
		}

		FileStream stream;
		try
		{
			stream = new FileStream(_files.GetFullPath(fileName), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1);
		}
		catch (FileNotFoundException)
		{
			Forget(fileName);
			return null;
		}
		catch (DirectoryNotFoundException)
		{
			Forget(fileName);
			return null;
		}

		using (stream)
		{
			var length  = stream.Length;
			var rebuilt = rebuild || state.ForceRebuild || snapshot == null;
			EntryIndexSnapshot? next = null;

			if (!rebuilt)
			{
				// A pure append leaves the sampled bytes of the indexed part as they were; anything else is a rewrite.
				if (length >= snapshot!.IndexedLength && _fingerprint.Compute(stream, snapshot.Records, snapshot.Count, snapshot.IndexedLength) == snapshot.Fingerprint)
				{
					next = length == snapshot.IndexedLength
						? Restamp(snapshot, stamp)
						: AppendTail(stream, snapshot, stamp, length);
				}

				rebuilt = next == null;
			}

			if (rebuilt)
				next = Rebuild(stream, snapshot, stamp, length);

			state.ForceRebuild = false;
			state.Snapshot     = next;

			if (rebuilt)
			{
				Persist(fileName, state);
			}
			else if (!ReferenceEquals(next, snapshot))
			{
				if (next!.ContentVersion != snapshot!.ContentVersion)
					state.Dirty = true;

				if (DateTime.UtcNow - state.LastPersistUtc >= Constants.ContentStore.PersistInterval)
					PersistIfDirty(fileName, state);
			}

			return next;
		}
	}

	/// <summary>The same index, agreed with a file whose last write time moved but whose indexed part did not.</summary>
	private static EntryIndexSnapshot Restamp(EntryIndexSnapshot snapshot, SessionFileStampModel stamp) => new()
	{
		FileId          = snapshot.FileId,
		Records         = snapshot.Records,
		Count           = snapshot.Count,
		IndexedLength   = snapshot.IndexedLength,
		LastWriteTicks  = stamp.LastWriteUtc.UtcTicks,
		ContentVersion  = snapshot.ContentVersion,
		Fingerprint     = snapshot.Fingerprint,
		LastProfileName = snapshot.LastProfileName,
		LastModelId     = snapshot.LastModelId,
		LastEffort      = snapshot.LastEffort,
	};

	private EntryIndexSnapshot Rebuild(FileStream stream, EntryIndexSnapshot? previous, SessionFileStampModel stamp, long length)
	{
		var headers = _scanner.Scan(stream, _scanner.FirstLineOffset(stream), length);
		var records = new EntryIndexRecord[Math.Max(headers.Count + headers.Count / 2, 16)];
		FillRecords(records, 0, headers, length);

		var (profile, model, effort) = LastMetadata(headers, null, null, null);
		return new EntryIndexSnapshot
		{
			FileId          = Guid.NewGuid(),
			Records         = records,
			Count           = headers.Count,
			IndexedLength   = length,
			LastWriteTicks  = stamp.LastWriteUtc.UtcTicks,
			ContentVersion  = (previous?.ContentVersion ?? 0) + 1,
			Fingerprint     = _fingerprint.Compute(stream, records, headers.Count, length),
			LastProfileName = profile,
			LastModelId     = model,
			LastEffort      = effort,
		};
	}

	/// <summary>
	/// Indexes the bytes past the old end. The last old entry is scanned again because it extends to the end of the file;
	/// null when its header is gone, which means the file was not simply appended to.
	/// </summary>
	private EntryIndexSnapshot? AppendTail(FileStream stream, EntryIndexSnapshot snapshot, SessionFileStampModel stamp, long length)
	{
		var keep = Math.Max(snapshot.Count - 1, 0);
		var from = snapshot.Count > 0 ? snapshot.Records[keep].Offset : _scanner.FirstLineOffset(stream);

		var headers = _scanner.Scan(stream, from, length);
		if (snapshot.Count > 0 && (headers.Count == 0 || headers[0].Offset != from))
			return null;

		var count   = keep + headers.Count;
		var tail    = new EntryIndexRecord[headers.Count];
		FillRecords(tail, keep, headers, length);

		// The array is shared with older snapshots, which never look past their own count, as long as no record they see changes.
		var unchanged = snapshot.Count == 0 || (headers.Count > 0 && snapshot.Records[keep] == tail[0]);
		EntryIndexRecord[] records;
		if (unchanged && snapshot.Records.Length >= count)
		{
			records = snapshot.Records;
		}
		else
		{
			records = new EntryIndexRecord[Math.Max(count + count / 2, 16)];
			Array.Copy(snapshot.Records, records, keep);
		}

		Array.Copy(tail, 0, records, keep, tail.Length);

		var (profile, model, effort) = LastMetadata(headers, snapshot.LastProfileName, snapshot.LastModelId, snapshot.LastEffort);
		return new EntryIndexSnapshot
		{
			FileId          = snapshot.FileId,
			Records         = records,
			Count           = count,
			IndexedLength   = length,
			LastWriteTicks  = stamp.LastWriteUtc.UtcTicks,
			ContentVersion  = snapshot.ContentVersion + 1,
			Fingerprint     = _fingerprint.Compute(stream, records, count, length),
			LastProfileName = profile,
			LastModelId     = model,
			LastEffort      = effort,
		};
	}

	private static void FillRecords(EntryIndexRecord[] target, int firstOrdinal, List<ScannedHeader> headers, long end)
	{
		for (var i = 0; i < headers.Count; i++)
		{
			var header = headers[i];
			var next   = i + 1 < headers.Count ? headers[i + 1].Offset : end;
			var flags  = EntryIndexFlags.None;
			if (header.ProfileName != null)
				flags |= EntryIndexFlags.HasProfile;
			if (header.ModelId != null)
				flags |= EntryIndexFlags.HasModel;
			if (header.Effort != null)
				flags |= EntryIndexFlags.HasEffort;

			target[i] = new EntryIndexRecord(
				firstOrdinal + i,
				header.Offset,
				(int)Math.Min(int.MaxValue, next - header.Offset),
				header.Timestamp.UtcTicks,
				RoleCodeOf(header.Role),
				flags);
		}
	}

	private static EntryRoleCode RoleCodeOf(string role) => role switch
	{
		Constants.SessionFile.RoleUser       => EntryRoleCode.User,
		Constants.SessionFile.RoleAssistant  => EntryRoleCode.Assistant,
		Constants.SessionFile.RoleSystem     => EntryRoleCode.System,
		Constants.SessionFile.RoleCompaction => EntryRoleCode.Compaction,
		_                                    => EntryRoleCode.Other,
	};

	private static (string? Profile, string? Model, string? Effort) LastMetadata(List<ScannedHeader> headers, string? profile, string? model, string? effort)
	{
		foreach (var header in headers)
		{
			profile = header.ProfileName ?? profile;
			model   = header.ModelId ?? model;
			effort  = header.Effort ?? effort;
		}

		return (profile, model, effort);
	}

	private EntryIndexSnapshot? LoadSidecar(string fileName)
	{
		try
		{
			var path = SidecarPath(fileName);
			return File.Exists(path) ? _codec.TryRead(File.ReadAllBytes(path)) : null;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return null;
		}
	}

	private void PersistIfDirty(string fileName, FileIndexState state)
	{
		if (state.Dirty)
			Persist(fileName, state);
	}

	private void Persist(string fileName, FileIndexState state)
	{
		var snapshot = state.Snapshot;
		if (snapshot == null)
			return;

		try
		{
			Directory.CreateDirectory(_directory);
			var path = SidecarPath(fileName);
			var temp = path + Constants.ContentStore.TempFileExtension;
			File.WriteAllBytes(temp, _codec.Write(snapshot));
			File.Move(temp, path, overwrite: true);
			state.Dirty = false;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			// A sidecar that could not be written is rebuilt from the session file next time; the index stays correct in memory.
			state.Dirty = true;
		}
		finally
		{
			state.LastPersistUtc = DateTime.UtcNow;
		}
	}

	private void EvictSurplus()
	{
		if (_states.Count <= Constants.ContentStore.MaxIndexedFiles)
			return;

		lock (_evictGate)
		{
			while (_states.Count > Constants.ContentStore.MaxIndexedFiles)
			{
				var oldest = _states.MinBy(pair => pair.Value.LastUsed);
				lock (oldest.Value.Gate)
					PersistIfDirty(oldest.Key, oldest.Value);

				_states.TryRemove(oldest.Key, out _);
			}
		}
	}

	private string SidecarPath(string fileName) =>
		Path.Combine(_directory, Path.GetFileName(fileName) + Constants.ContentStore.IndexFileExtension);

	private static void TryDelete(string path)
	{
		try
		{
			File.Delete(path);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			// A leftover sidecar is harmless: it fails validation against the rewritten file and is replaced.
		}
	}
}
